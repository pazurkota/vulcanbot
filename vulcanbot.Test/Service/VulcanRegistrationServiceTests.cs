using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using vulcanbot.Service;
using vulcanbot.Service.Hebe;
using vulcanbot.Test.Helpers;

namespace vulcanbot.Test.Service;

public class VulcanRegistrationServiceTests
{
    private const string Token = "3s1abcd";
    private const string Pin = "123456";
    private const string Symbol = "PowiatWulkanowy";
    private const string ExpectedUrl = "https://lekcjaplus.vulcan.net.pl/powiatwulkanowy/api/mobile/register/new";
    private const string RestUrl = "https://lekcjaplus.vulcan.net.pl/powiatwulkanowy/";

    private const string SuccessResponse = """
        {
          "Envelope": { "LoginId": 2137, "UserLogin": "jan@example.com", "UserName": "jan@example.com", "RestURL": "https://lekcjaplus.vulcan.net.pl/powiatwulkanowy/" },
          "Status": { "Code": 0, "Message": "OK" },
          "RequestId": "c3b5a1f2", "Timestamp": 1793630593000, "TimestampFormatted": "2026-11-02 14:43:13"
        }
        """;

    private FakeHttpMessageHandler _handler = null!;
    private HttpClient _client = null!;
    private VulcanRegistrationService _service = null!;

    [SetUp]
    public void Setup()
    {
        _handler = new FakeHttpMessageHandler();
        _handler.RespondTo(VulcanRoutingService.RoutingRulesUrl, "3S1,https://lekcjaplus.vulcan.net.pl\r\n", contentType: "text/plain");
        _handler.RespondTo(ExpectedUrl, SuccessResponse);
        _client = new HttpClient(_handler);
        _service = new VulcanRegistrationService(
            new HebeApiClient(_client, TimeProvider.System),
            new VulcanRoutingService(_client));
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _handler.Dispose();
    }

    private CapturedRequest RegisterRequest => _handler.Requests.Single(r => r.Uri!.ToString() == ExpectedUrl);

    [Test]
    public async Task RegisterDeviceAsync_PostsToRoutedBaseUrlWithLowercaseSymbol()
    {
        await _service.RegisterDeviceAsync(Token, Pin, Symbol, CancellationToken.None);

        Assert.That(RegisterRequest.Method, Is.EqualTo(HttpMethod.Post));
    }

    [Test]
    public async Task RegisterDeviceAsync_SendsRegisterEnvelope()
    {
        await _service.RegisterDeviceAsync(Token, Pin, Symbol, CancellationToken.None);

        using var json = JsonDocument.Parse(RegisterRequest.Body);
        var envelope = json.RootElement.GetProperty("Envelope");
        Assert.Multiple(() =>
        {
            Assert.That(envelope.GetProperty("OS").GetString(), Is.EqualTo("Android"));
            Assert.That(envelope.GetProperty("DeviceModel").GetString(), Is.EqualTo(HebeConstants.DeviceModel));
            Assert.That(envelope.GetProperty("CertificateType").GetString(), Is.EqualTo("X509"));
            Assert.That(envelope.GetProperty("PIN").GetString(), Is.EqualTo(Pin));
            Assert.That(envelope.GetProperty("SecurityToken").GetString(), Is.EqualTo("3S1ABCD"));
            Assert.That(Guid.TryParse(envelope.GetProperty("SelfIdentifier").GetString(), out _), Is.True);
        });
    }

    [Test]
    public async Task RegisterDeviceAsync_SentCertificateAndThumbprintMatchReturnedKeys()
    {
        var result = await _service.RegisterDeviceAsync(Token, Pin, Symbol, CancellationToken.None);

        using var json = JsonDocument.Parse(RegisterRequest.Body);
        var envelope = json.RootElement.GetProperty("Envelope");
        using var sent = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(envelope.GetProperty("Certificate").GetString()!));
        using var returned = X509Certificate2.CreateFromPem(result.DeviceKeys.CertificatePem);

        Assert.Multiple(() =>
        {
            Assert.That(sent.RawData, Is.EqualTo(returned.RawData));
            Assert.That(envelope.GetProperty("CertificateThumbprint").GetString(), Is.EqualTo(result.DeviceKeys.Fingerprint));
            Assert.That(json.RootElement.GetProperty("CertificateId").GetString(), Is.EqualTo(result.DeviceKeys.Fingerprint));
        });
    }

    [Test]
    public async Task RegisterDeviceAsync_RequestIsSignedWithReturnedDeviceKey()
    {
        var result = await _service.RegisterDeviceAsync(Token, Pin, Symbol, CancellationToken.None);

        var request = RegisterRequest;
        var signature = request.Headers["Signature"].Split("SHA256withRSA(")[1].TrimEnd(')');
        var signedData = request.Headers["vCanonicalUrl"] + request.Headers["Digest"]["SHA-256=".Length..] + request.Headers["vDate"];

        using var cert = X509Certificate2.CreateFromPem(result.DeviceKeys.CertificatePem);
        using var publicKey = cert.GetRSAPublicKey()!;
        Assert.Multiple(() =>
        {
            Assert.That(request.Headers["Signature"], Does.StartWith($"keyId=\"{result.DeviceKeys.Fingerprint}\""));
            Assert.That(publicKey.VerifyData(System.Text.Encoding.UTF8.GetBytes(signedData), Convert.FromBase64String(signature),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), Is.True);
        });
    }

    [Test]
    public async Task RegisterDeviceAsync_ReturnsDataFromResponse()
    {
        var result = await _service.RegisterDeviceAsync(Token, Pin, Symbol, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Symbol, Is.EqualTo("powiatwulkanowy"));
            Assert.That(result.RestUrl, Is.EqualTo(RestUrl));
            Assert.That(result.LoginId, Is.EqualTo(2137));
            Assert.That(result.UserLogin, Is.EqualTo("jan@example.com"));
            Assert.That(result.UserName, Is.EqualTo("jan@example.com"));
        });
    }

    [Test]
    public async Task RegisterDeviceAsync_ReturnsMatchingRsa2048KeyPair()
    {
        var result = await _service.RegisterDeviceAsync(Token, Pin, Symbol, CancellationToken.None);

        using var cert = X509Certificate2.CreateFromPem(result.DeviceKeys.CertificatePem, result.DeviceKeys.PrivatePemKey);
        using var rsa = cert.GetRSAPrivateKey()!;
        Assert.Multiple(() =>
        {
            Assert.That(rsa.KeySize, Is.EqualTo(2048));
            Assert.That(cert.HasPrivateKey, Is.True);
            Assert.That(result.DeviceKeys.Fingerprint, Is.EqualTo(Convert.ToHexStringLower(SHA1.HashData(cert.RawData))));
        });
    }

    [Test]
    public async Task RegisterDeviceAsync_GeneratesNewKeysOnEachCall()
    {
        var first = await _service.RegisterDeviceAsync(Token, Pin, Symbol, CancellationToken.None);
        var second = await _service.RegisterDeviceAsync(Token, Pin, Symbol, CancellationToken.None);

        Assert.That(first.DeviceKeys.Fingerprint, Is.Not.EqualTo(second.DeviceKeys.Fingerprint));
    }

    [Test]
    public void RegisterDeviceAsync_WrongPin_ThrowsInvalidPin()
    {
        _handler.RespondTo(ExpectedUrl, """{"Envelope":null,"Status":{"Code":203,"Message":"Niepoprawny PIN"}}""");

        var ex = Assert.ThrowsAsync<VulcanApiException>(() =>
            _service.RegisterDeviceAsync(Token, Pin, Symbol, CancellationToken.None));

        Assert.That(ex!.Kind, Is.EqualTo(VulcanErrorKind.InvalidPin));
    }

    [Test]
    public void RegisterDeviceAsync_UnknownTokenPrefix_ThrowsBeforeContactingVulcan()
    {
        var ex = Assert.ThrowsAsync<VulcanApiException>(() =>
            _service.RegisterDeviceAsync("XX9ABC", Pin, Symbol, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.Kind, Is.EqualTo(VulcanErrorKind.InvalidToken));
            Assert.That(_handler.Requests.Select(r => r.Uri!.ToString()), Is.EqualTo(new[] { VulcanRoutingService.RoutingRulesUrl }));
        });
    }

    [Test]
    public void RegisterDeviceAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.That(() => _service.RegisterDeviceAsync(Token, Pin, Symbol, cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }
}
