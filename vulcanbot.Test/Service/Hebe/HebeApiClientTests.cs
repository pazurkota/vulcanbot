using System.Net;
using System.Text.Json;
using vulcanbot.Dto;
using vulcanbot.Service;
using vulcanbot.Service.Hebe;
using vulcanbot.Test.Helpers;

namespace vulcanbot.Test.Service.Hebe;

public class HebeApiClientTests
{
    private const string Url = "https://lekcjaplus.vulcan.net.pl/powiatwulkanowy/api/mobile/register/new";
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 14, 43, 13, TimeSpan.Zero);
    private static readonly DeviceKey Key = VulcanRegistrationService.GenerateDeviceKey().Key;

    private record Ping(string Value);
    private record Pong(int Answer);

    private FakeHttpMessageHandler _handler = null!;
    private HttpClient _client = null!;
    private HebeApiClient _api = null!;

    [SetUp]
    public void Setup()
    {
        _handler = new FakeHttpMessageHandler { DefaultBody = Ok("""{"Answer":42}""") };
        _client = new HttpClient(_handler);
        _api = new HebeApiClient(_client, new FixedTimeProvider(Now));
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _handler.Dispose();
    }

    private static string Ok(string envelope) =>
        $$"""{"Envelope":{{envelope}},"Status":{"Code":0,"Message":"OK"},"RequestId":"x","Timestamp":0}""";

    private static string Error(int code, string message) =>
        $$"""{"Envelope":null,"Status":{"Code":{{code}},"Message":"{{message}}"},"RequestId":"x","Timestamp":0}""";

    [Test]
    public async Task PostAsync_ReturnsUnwrappedEnvelope()
    {
        var result = await _api.PostAsync<Ping, Pong>(Url, new Ping("hi"), Key, CancellationToken.None);

        Assert.That(result, Is.EqualTo(new Pong(42)));
    }

    [Test]
    public async Task PostAsync_WrapsBodyInHebeRequestEnvelope()
    {
        await _api.PostAsync<Ping, Pong>(Url, new Ping("hi"), Key, CancellationToken.None);

        using var json = JsonDocument.Parse(_handler.Requests.Single().Body);
        var root = json.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("AppName").GetString(), Is.EqualTo(HebeConstants.AppName));
            Assert.That(root.GetProperty("AppVersion").GetString(), Is.EqualTo(HebeConstants.AppVersion));
            Assert.That(root.GetProperty("CertificateId").GetString(), Is.EqualTo(Key.Fingerprint));
            Assert.That(root.GetProperty("API").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("FirebaseToken").GetString(), Is.Empty);
            Assert.That(Guid.TryParse(root.GetProperty("RequestId").GetString(), out _), Is.True);
            Assert.That(root.GetProperty("Timestamp").GetInt64(), Is.EqualTo(Now.ToUnixTimeMilliseconds()));
            Assert.That(root.GetProperty("TimestampFormatted").GetString(), Is.EqualTo("2026-11-02 14:43:13"));
            Assert.That(root.GetProperty("Envelope").GetProperty("Value").GetString(), Is.EqualTo("hi"));
        });
    }

    [Test]
    public async Task PostAsync_SendsSignedHeadersMatchingBody()
    {
        await _api.PostAsync<Ping, Pong>(Url, new Ping("hi"), Key, CancellationToken.None);

        var request = _handler.Requests.Single();
        var expected = HebeSigner.Sign(Key.Fingerprint, Key.PrivatePemKey, Url, request.Body, Now);
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(request.ContentType, Is.EqualTo("application/json"));
            Assert.That(request.Headers["User-Agent"], Is.EqualTo(HebeConstants.UserAgent));
            Assert.That(request.Headers["vOS"], Is.EqualTo("Android"));
            Assert.That(request.Headers["vDeviceModel"], Is.EqualTo(HebeConstants.DeviceModel));
            Assert.That(request.Headers["vAPI"], Is.EqualTo("1"));
            Assert.That(request.Headers["vDate"], Is.EqualTo("Mon, 02 Nov 2026 14:43:13 GMT"));
            Assert.That(request.Headers["vCanonicalUrl"], Is.EqualTo("api%2fmobile%2fregister%2fnew"));
            Assert.That(request.Headers["Digest"], Is.EqualTo(expected.Digest));
            Assert.That(request.Headers["Signature"], Is.EqualTo(expected.Signature));
        });
    }

    [Test]
    public async Task GetAsync_SendsNoBodyAndNoDigest()
    {
        const string getUrl = "https://lekcjaplus.vulcan.net.pl/powiatwulkanowy/123/api/mobile/register/hebe";

        await _api.GetAsync<Pong>(getUrl, Key, CancellationToken.None);

        var request = _handler.Requests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Get));
            Assert.That(request.Body, Is.Empty);
            Assert.That(request.Headers.ContainsKey("Digest"), Is.False);
            Assert.That(request.Headers["Signature"], Does.Contain("headers=\"vCanonicalUrl vDate\""));
        });
    }

    [TestCase(108, "Certyfikat nie istnieje", VulcanErrorKind.UnauthorizedCertificate)]
    [TestCase(200, "Token niepoprawny", VulcanErrorKind.InvalidToken)]
    [TestCase(203, "PIN niepoprawny", VulcanErrorKind.InvalidPin)]
    [TestCase(204, "Token wygasł", VulcanErrorKind.ExpiredToken)]
    [TestCase(-1, "Brak symbolu", VulcanErrorKind.InvalidSymbol)]
    [TestCase(100, "Signature: abc", VulcanErrorKind.InvalidSignature)]
    [TestCase(100, "Something else", VulcanErrorKind.Unknown)]
    [TestCase(999, "Nieznany", VulcanErrorKind.Unknown)]
    public void PostAsync_NonZeroStatus_ThrowsWithMappedKind(int code, string message, VulcanErrorKind kind)
    {
        _handler.DefaultBody = Error(code, message);

        var ex = Assert.ThrowsAsync<VulcanApiException>(() =>
            _api.PostAsync<Ping, Pong>(Url, new Ping("hi"), Key, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.Kind, Is.EqualTo(kind));
            Assert.That(ex.StatusCode, Is.EqualTo(code));
            Assert.That(ex.Message, Does.Contain(message));
        });
    }

    [Test]
    public void PostAsync_ErrorStatusWithHttp200_IsNotTreatedAsSuccess()
    {
        _handler.DefaultStatus = HttpStatusCode.OK;
        _handler.DefaultBody = Error(203, "PIN niepoprawny");

        Assert.ThrowsAsync<VulcanApiException>(() =>
            _api.PostAsync<Ping, Pong>(Url, new Ping("hi"), Key, CancellationToken.None));
    }

    [Test]
    public void PostAsync_Http500WithArgumentException_ThrowsInvalidSymbol()
    {
        _handler.DefaultStatus = HttpStatusCode.InternalServerError;
        _handler.DefaultBody = "<html>System.ArgumentException: Unknown tenant</html>";

        var ex = Assert.ThrowsAsync<VulcanApiException>(() =>
            _api.PostAsync<Ping, Pong>(Url, new Ping("hi"), Key, CancellationToken.None));

        Assert.That(ex!.Kind, Is.EqualTo(VulcanErrorKind.InvalidSymbol));
    }

    [TestCase(HttpStatusCode.NotFound)]
    [TestCase(HttpStatusCode.BadGateway)]
    public void PostAsync_HttpErrorWithoutEnvelope_ThrowsHttpError(HttpStatusCode status)
    {
        _handler.DefaultStatus = status;
        _handler.DefaultBody = "<html>error</html>";

        var ex = Assert.ThrowsAsync<VulcanApiException>(() =>
            _api.PostAsync<Ping, Pong>(Url, new Ping("hi"), Key, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.Kind, Is.EqualTo(VulcanErrorKind.HttpError));
            Assert.That(ex.StatusCode, Is.EqualTo((int)status));
        });
    }

    [Test]
    public void PostAsync_Http200WithGarbage_ThrowsInvalidResponse()
    {
        _handler.DefaultBody = "not json";

        var ex = Assert.ThrowsAsync<VulcanApiException>(() =>
            _api.PostAsync<Ping, Pong>(Url, new Ping("hi"), Key, CancellationToken.None));

        Assert.That(ex!.Kind, Is.EqualTo(VulcanErrorKind.InvalidResponse));
    }

    [Test]
    public void PostAsync_SuccessStatusWithoutEnvelope_ThrowsInvalidResponse()
    {
        _handler.DefaultBody = """{"Envelope":null,"Status":{"Code":0,"Message":"OK"}}""";

        var ex = Assert.ThrowsAsync<VulcanApiException>(() =>
            _api.PostAsync<Ping, Pong>(Url, new Ping("hi"), Key, CancellationToken.None));

        Assert.That(ex!.Kind, Is.EqualTo(VulcanErrorKind.InvalidResponse));
    }
}
