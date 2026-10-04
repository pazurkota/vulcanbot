using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using vulcanbot.Service;

namespace vulcanbot.Test.Service;

public class VulcanRegistrationServiceTests
{
    private const string Token = "3S1ABCD";
    private const string Pin = "123456";
    private const string Symbol = "powiatwulkanowy";
    private const string ExpectedUri = $"https://certyfikaty.vulcan.net.pl/{Symbol}/api/mobile/register/new";

    private FakeHttpMessageHandler _handler = null!;
    private HttpClient _client = null!;
    private VulcanRegistrationService _service = null!;

    [SetUp]
    public void Setup()
    {
        _handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "{}");
        _client = new HttpClient(_handler);
        _service = new VulcanRegistrationService(_client);
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _handler.Dispose();
    }

    [Test]
    public async Task RegisterDeviceAsync_SendsPostToSymbolSpecificEndpoint()
    {
        await _service.RegisterDeviceAsync(Token, Pin, Symbol, CancellationToken.None);

        Assert.That(_handler.Requests, Has.Count.EqualTo(1));
        var request = _handler.Requests[0];
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(request.Uri, Is.EqualTo(new Uri(ExpectedUri)));
        });
    }

    [Test]
    public async Task RegisterDeviceAsync_SetsVulcanHeaders()
    {
        await _service.RegisterDeviceAsync(Token, Pin, Symbol, CancellationToken.None);

        var headers = _handler.Requests[0].Headers;
        Assert.Multiple(() =>
        {
            Assert.That(headers["User-Agent"], Is.EqualTo("Dart 3.0 (dart:io)"));
            Assert.That(headers["vOS"], Is.EqualTo("Android"));
            Assert.That(headers["vDeviceModel"], Is.EqualTo(".NET 10"));
            Assert.That(headers["vAPI"], Is.EqualTo("1"));
            Assert.That(DateTimeOffset.TryParse(headers["vDate"], out var vDate), Is.True);
            Assert.That(vDate, Is.EqualTo(DateTimeOffset.UtcNow).Within(TimeSpan.FromMinutes(1)));
        });
    }

    [Test]
    public async Task RegisterDeviceAsync_SendsJsonPayloadWithCredentialsAndDeviceInfo()
    {
        await _service.RegisterDeviceAsync(Token, Pin, Symbol, CancellationToken.None);

        var request = _handler.Requests[0];
        Assert.That(request.ContentType, Is.EqualTo("application/json"));

        using var json = JsonDocument.Parse(request.Body);
        var root = json.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("Pin").GetString(), Is.EqualTo(Pin));
            Assert.That(root.GetProperty("Token").GetString(), Is.EqualTo(Token));
            Assert.That(root.GetProperty("AppVersion").GetString(), Is.EqualTo("24.0.0"));
            Assert.That(Guid.TryParse(root.GetProperty("DeviceId").GetString(), out _), Is.True);
            Assert.That(root.GetProperty("DeviceName").GetString(), Is.EqualTo("vulcanbot"));
            Assert.That(root.GetProperty("DeviceModel").GetString(), Is.EqualTo(".NET 10"));
            Assert.That(root.GetProperty("DeviceType").GetString(), Is.EqualTo("Android"));
            Assert.That(root.GetProperty("CertificateType").GetString(), Is.EqualTo("X509"));
        });
    }

    [Test]
    public async Task RegisterDeviceAsync_SentCertificateMatchesReturnedCertificate()
    {
        var result = await _service.RegisterDeviceAsync(Token, Pin, Symbol, CancellationToken.None);

        using var json = JsonDocument.Parse(_handler.Requests[0].Body);
        var sentCertBytes = Convert.FromBase64String(json.RootElement.GetProperty("Certificate").GetString()!);
        using var sentCert = X509CertificateLoader.LoadCertificate(sentCertBytes);
        using var returnedCert = X509Certificate2.CreateFromPem(result.DeviceKeys.CertificatePem);

        Assert.That(sentCert.RawData, Is.EqualTo(returnedCert.RawData));
    }

    [Test]
    public async Task RegisterDeviceAsync_ReturnsSymbolAndEndpointUrl()
    {
        var result = await _service.RegisterDeviceAsync(Token, Pin, Symbol, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Symbol, Is.EqualTo(Symbol));
            Assert.That(result.EndpointUrl, Is.EqualTo(ExpectedUri));
        });
    }

    [Test]
    public async Task RegisterDeviceAsync_ReturnsSelfSignedRsa2048Certificate()
    {
        var result = await _service.RegisterDeviceAsync(Token, Pin, Symbol, CancellationToken.None);

        using var cert = X509Certificate2.CreateFromPem(result.DeviceKeys.CertificatePem);
        using var publicKey = cert.GetRSAPublicKey();
        Assert.Multiple(() =>
        {
            Assert.That(cert.Subject, Is.EqualTo("CN=OpenVulcan"));
            Assert.That(cert.Issuer, Is.EqualTo(cert.Subject));
            Assert.That(publicKey, Is.Not.Null);
            Assert.That(publicKey!.KeySize, Is.EqualTo(2048));
            Assert.That(cert.NotBefore.ToUniversalTime(), Is.LessThanOrEqualTo(DateTime.UtcNow));
            Assert.That(cert.NotAfter.ToUniversalTime(), Is.GreaterThan(DateTime.UtcNow.AddMonths(11)));
        });
    }

    [Test]
    public async Task RegisterDeviceAsync_PrivateKeyMatchesCertificate()
    {
        var result = await _service.RegisterDeviceAsync(Token, Pin, Symbol, CancellationToken.None);

        Assert.That(result.DeviceKeys.PrivatePemKey, Does.StartWith("-----BEGIN PRIVATE KEY-----"));

        using var certWithKey = X509Certificate2.CreateFromPem(
            result.DeviceKeys.CertificatePem, result.DeviceKeys.PrivatePemKey);
        using var privateKey = certWithKey.GetRSAPrivateKey()!;
        using var publicKey = certWithKey.GetRSAPublicKey()!;

        var data = "vulcanbot"u8.ToArray();
        var signature = privateKey.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        Assert.That(publicKey.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
            Is.True);
    }

    [Test]
    public async Task RegisterDeviceAsync_FingerprintIsLowercaseSha1ThumbprintOfCertificate()
    {
        var result = await _service.RegisterDeviceAsync(Token, Pin, Symbol, CancellationToken.None);

        using var cert = X509Certificate2.CreateFromPem(result.DeviceKeys.CertificatePem);
        Assert.Multiple(() =>
        {
            Assert.That(result.DeviceKeys.Fingerprint, Is.EqualTo(cert.Thumbprint.ToLowerInvariant()));
            Assert.That(result.DeviceKeys.Fingerprint, Does.Match("^[0-9a-f]{40}$"));
        });
    }

    [Test]
    public async Task RegisterDeviceAsync_GeneratesNewKeysAndDeviceIdOnEachCall()
    {
        var first = await _service.RegisterDeviceAsync(Token, Pin, Symbol, CancellationToken.None);
        var second = await _service.RegisterDeviceAsync(Token, Pin, Symbol, CancellationToken.None);

        using var firstJson = JsonDocument.Parse(_handler.Requests[0].Body);
        using var secondJson = JsonDocument.Parse(_handler.Requests[1].Body);
        Assert.Multiple(() =>
        {
            Assert.That(second.DeviceKeys.PrivatePemKey, Is.Not.EqualTo(first.DeviceKeys.PrivatePemKey));
            Assert.That(second.DeviceKeys.Fingerprint, Is.Not.EqualTo(first.DeviceKeys.Fingerprint));
            Assert.That(secondJson.RootElement.GetProperty("DeviceId").GetString(),
                Is.Not.EqualTo(firstJson.RootElement.GetProperty("DeviceId").GetString()));
        });
    }

    [TestCase(HttpStatusCode.BadRequest)]
    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.NotFound)]
    [TestCase(HttpStatusCode.InternalServerError)]
    public void RegisterDeviceAsync_NonSuccessStatus_ThrowsInvalidOperationException(HttpStatusCode statusCode)
    {
        _handler.StatusCode = statusCode;

        var ex = Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RegisterDeviceAsync(Token, Pin, Symbol, CancellationToken.None));

        Assert.That(ex!.Message, Does.StartWith("Registration failed."));
        Assert.That(ex.Message, Does.Contain($"Status: {statusCode}"));
    }

    [Test]
    public void RegisterDeviceAsync_NonSuccessStatus_IncludesResponseBodyInMessage()
    {
        const string errorBody = """{"Status":{"Code":200,"Message":"Invalid PIN"}}""";
        _handler.StatusCode = HttpStatusCode.BadRequest;
        _handler.ResponseBody = errorBody;

        var ex = Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RegisterDeviceAsync(Token, Pin, Symbol, CancellationToken.None));

        Assert.That(ex!.Message, Is.EqualTo($"Registration failed. Status: BadRequest. Message: {errorBody}"));
    }

    [Test]
    public void RegisterDeviceAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.That(() => _service.RegisterDeviceAsync(Token, Pin, Symbol, cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri? Uri,
        IReadOnlyDictionary<string, string> Headers,
        string? ContentType,
        string Body);

    private sealed class FakeHttpMessageHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; set; } = statusCode;
        public string ResponseBody { get; set; } = responseBody;
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Request is disposed by the service after SendAsync returns, so capture everything now.
            var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(" ", h.Value));
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri,
                headers,
                request.Content?.Headers.ContentType?.MediaType,
                body));

            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseBody)
            };
        }
    }
}
