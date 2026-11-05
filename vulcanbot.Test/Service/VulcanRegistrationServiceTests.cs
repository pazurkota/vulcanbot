using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using vulcanbot.Service;
using vulcanbot.Service.Hebe;
using static vulcanbot.Test.Service.EduVulcanApParserTests;

namespace vulcanbot.Test.Service;

public class VulcanRegistrationServiceTests
{
    private const string Tenant = "powiatwulkanowy";
    private const string RegisterUri = $"https://lekcjaplus.vulcan.net.pl/{Tenant}/api/mobile/register/jwt";
    private const string AccountsUri = $"https://lekcjaplus.vulcan.net.pl/{Tenant}/api/mobile/register/hebe?mode=2";

    private const string Ok = """{"Status":{"Code":0,"Message":"OK"},"Envelope":{}}""";

    private const string Accounts = """
        {"Status":{"Code":0,"Message":"OK"},"Envelope":[{
          "TopLevelPartition":"powiatwulkanowy","Partition":"powiatwulkanowy-000088","ClassDisplay":"8A",
          "Unit":{"Id":1,"Symbol":"000088","Short":"SP1","RestURL":"https://lekcjaplus.vulcan.net.pl/powiatwulkanowy/000088/api","Name":"Szkoła Podstawowa nr 1","DisplayName":"SP1"},
          "ConstituentUnit":{"Id":2,"Short":"SP1","Name":"Szkoła Podstawowa nr 1","Address":null},
          "Pupil":{"Id":7,"LoginId":70,"LoginValue":null,"FirstName":"Jan","SecondName":null,"Surname":"Uczeń","Sex":true},
          "Periods":[]
        }]}
        """;

    private static readonly string Token = Jwt(Tenant);

    private FakeHttpMessageHandler _handler = null!;
    private HttpClient _client = null!;
    private VulcanRegistrationService _service = null!;

    [SetUp]
    public void Setup()
    {
        _handler = new FakeHttpMessageHandler();
        _handler.Respond("register/jwt", Ok);
        _handler.Respond("register/hebe", Accounts);
        _client = new HttpClient(_handler);
        _service = new VulcanRegistrationService(new HebeClient(_client));
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _handler.Dispose();
    }

    private Task<vulcanbot.Dto.RegistrationResult> RegisterAsync(string? ap = null) =>
        _service.RegisterDeviceAsync(ap ?? Ap(Token), CancellationToken.None);

    [Test]
    public async Task RegisterDeviceAsync_RegistersJwtThenFetchesAccountsForTenant()
    {
        await RegisterAsync();

        Assert.That(_handler.Requests.Select(r => (r.Method, r.Uri!.OriginalString)), Is.EqualTo(new[]
        {
            (HttpMethod.Post, RegisterUri),
            (HttpMethod.Get, AccountsUri)
        }));
    }

    [Test]
    public async Task RegisterDeviceAsync_SetsAppHeaders()
    {
        await RegisterAsync();

        var headers = _handler.Requests[0].Headers;
        Assert.Multiple(() =>
        {
            Assert.That(headers["User-Agent"], Is.EqualTo(HebeClient.UserAgent));
            Assert.That(headers["vOS"], Is.EqualTo("Android"));
            Assert.That(headers["vDeviceModel"], Is.EqualTo(HebeClient.DeviceModel));
            Assert.That(headers["vVersionCode"], Is.EqualTo(HebeClient.AppVersionCode));
            Assert.That(headers["vAPI"], Is.EqualTo("1"));
            Assert.That(headers["vCanonicalUrl"], Is.EqualTo("api%2fmobile%2fregister%2fjwt"));
            Assert.That(DateTimeOffset.TryParse(headers["vDate"], out var vDate), Is.True);
            Assert.That(vDate, Is.EqualTo(DateTimeOffset.UtcNow).Within(TimeSpan.FromMinutes(1)));
            Assert.That(headers.ContainsKey("vHint"), Is.False);
        });
    }

    [Test]
    public async Task RegisterDeviceAsync_SendsRequestEnvelope()
    {
        await RegisterAsync();

        var request = _handler.Requests[0];
        Assert.That(request.ContentType, Is.EqualTo("application/json"));

        using var json = JsonDocument.Parse(request.Body);
        var root = json.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("AppName").GetString(), Is.EqualTo("DzienniczekPlus 3.0"));
            Assert.That(root.GetProperty("AppVersion").GetString(), Is.EqualTo(HebeClient.AppVersion));
            Assert.That(root.GetProperty("NotificationToken").GetString(), Is.Empty);
            Assert.That(root.GetProperty("API").GetInt32(), Is.EqualTo(1));
            Assert.That(Guid.TryParse(root.GetProperty("RequestId").GetString(), out _), Is.True);
            Assert.That(root.GetProperty("Timestamp").GetInt64(),
                Is.EqualTo(DateTimeOffset.UtcNow.ToUnixTimeSeconds()).Within(60));
            Assert.That(root.GetProperty("TimestampFormatted").GetString(),
                Does.Match(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$"));
        });
    }

    [Test]
    public async Task RegisterDeviceAsync_SendsTokensAndDeviceInEnvelope()
    {
        var result = await RegisterAsync();

        using var json = JsonDocument.Parse(_handler.Requests[0].Body);
        var envelope = json.RootElement.GetProperty("Envelope");
        Assert.Multiple(() =>
        {
            Assert.That(envelope.GetProperty("Tokens").EnumerateArray().Select(t => t.GetString()),
                Is.EqualTo(new[] { Token }));
            Assert.That(envelope.GetProperty("OS").GetString(), Is.EqualTo("Android"));
            Assert.That(envelope.GetProperty("DeviceModel").GetString(), Is.EqualTo(HebeClient.DeviceModel));
            Assert.That(Guid.TryParse(envelope.GetProperty("SelfIdentifier").GetString(), out _), Is.True);
            Assert.That(envelope.GetProperty("CertificateType").GetString(), Is.EqualTo("X509"));
            Assert.That(envelope.GetProperty("CertificateThumbprint").GetString(),
                Is.EqualTo(result.DeviceKeys.Fingerprint));
        });
    }

    [Test]
    public async Task RegisterDeviceAsync_SentCertificateMatchesReturnedCertificate()
    {
        var result = await RegisterAsync();

        using var json = JsonDocument.Parse(_handler.Requests[0].Body);
        var sentCertBytes = Convert.FromBase64String(
            json.RootElement.GetProperty("Envelope").GetProperty("Certificate").GetString()!);
        using var sentCert = X509CertificateLoader.LoadCertificate(sentCertBytes);
        using var returnedCert = X509Certificate2.CreateFromPem(result.DeviceKeys.CertificatePem);

        Assert.That(sentCert.RawData, Is.EqualTo(returnedCert.RawData));
    }

    [Test]
    public async Task RegisterDeviceAsync_SignsRequestsWithDeviceKey()
    {
        var result = await RegisterAsync();

        using var cert = X509Certificate2.CreateFromPem(result.DeviceKeys.CertificatePem);
        using var publicKey = cert.GetRSAPublicKey()!;

        var post = _handler.Requests[0];
        var digest = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(post.Body)));
        var get = _handler.Requests[1];

        Assert.Multiple(() =>
        {
            Assert.That(post.Headers["Digest"], Is.EqualTo($"SHA-256={digest}"));
            Assert.That(post.Headers["Signature"], Does.StartWith($"keyId=\"{result.DeviceKeys.Fingerprint}\""));
            Assert.That(Verify(publicKey, post, post.Headers["vCanonicalUrl"] + digest + post.Headers["vDate"]),
                Is.True);
            Assert.That(get.Headers.ContainsKey("Digest"), Is.False);
            Assert.That(Verify(publicKey, get, get.Headers["vCanonicalUrl"] + get.Headers["vDate"]), Is.True);
        });
    }

    [Test]
    public async Task RegisterDeviceAsync_ReturnsAccountsFromRegisterHebe()
    {
        var result = await RegisterAsync();

        var account = result.Accounts.Single();
        Assert.Multiple(() =>
        {
            Assert.That(account.TopLevelPartition, Is.EqualTo(Tenant));
            Assert.That(account.Unit.RestUrl, Is.EqualTo("https://lekcjaplus.vulcan.net.pl/powiatwulkanowy/000088/api"));
            Assert.That(account.Pupil.Id, Is.EqualTo(7));
            Assert.That(account.Pupil.FirstName, Is.EqualTo("Jan"));
        });
    }

    [Test]
    public async Task RegisterDeviceAsync_TokensFromTwoTenants_RegistersSameDeviceInBoth()
    {
        await RegisterAsync(Ap(Jwt("szkolaa"), Jwt("szkolab")));

        var registrations = _handler.Requests.Where(r => r.Method == HttpMethod.Post).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(registrations.Select(r => r.Uri!.AbsolutePath), Is.EqualTo(new[]
            {
                "/szkolaa/api/mobile/register/jwt",
                "/szkolab/api/mobile/register/jwt"
            }));
            Assert.That(registrations.Select(Thumbprint).Distinct().Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RegisterDeviceAsync_ReturnsSelfSignedRsa2048Certificate()
    {
        var result = await RegisterAsync();

        using var cert = X509Certificate2.CreateFromPem(result.DeviceKeys.CertificatePem);
        using var publicKey = cert.GetRSAPublicKey();
        Assert.Multiple(() =>
        {
            Assert.That(cert.Issuer, Is.EqualTo(cert.Subject));
            Assert.That(publicKey, Is.Not.Null);
            Assert.That(publicKey!.KeySize, Is.EqualTo(2048));
            Assert.That(cert.NotBefore.ToUniversalTime(), Is.LessThanOrEqualTo(DateTime.UtcNow));
            Assert.That(cert.NotAfter.ToUniversalTime(), Is.GreaterThan(DateTime.UtcNow.AddYears(10)));
        });
    }

    [Test]
    public async Task RegisterDeviceAsync_PrivateKeyMatchesCertificate()
    {
        var result = await RegisterAsync();

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
        var result = await RegisterAsync();

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
        var first = await RegisterAsync();
        var second = await RegisterAsync();

        Assert.Multiple(() =>
        {
            Assert.That(second.DeviceKeys.PrivatePemKey, Is.Not.EqualTo(first.DeviceKeys.PrivatePemKey));
            Assert.That(second.DeviceKeys.Fingerprint, Is.Not.EqualTo(first.DeviceKeys.Fingerprint));
            Assert.That(SelfIdentifier(_handler.Requests[2]), Is.Not.EqualTo(SelfIdentifier(_handler.Requests[0])));
        });
    }

    [Test]
    public void RegisterDeviceAsync_InvalidApInput_SendsNoRequests()
    {
        Assert.ThrowsAsync<FormatException>(() => RegisterAsync("not json"));

        Assert.That(_handler.Requests, Is.Empty);
    }

    [TestCase(HttpStatusCode.BadRequest)]
    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.InternalServerError)]
    public void RegisterDeviceAsync_HttpError_ThrowsHebeExceptionWithBody(HttpStatusCode statusCode)
    {
        _handler.Respond("register/jwt", "Bad things", statusCode);

        var ex = Assert.ThrowsAsync<HebeException>(() => RegisterAsync());

        Assert.That(ex!.Message, Is.EqualTo($"POST {RegisterUri} failed. Status: {statusCode}. Message: Bad things"));
    }

    [Test]
    public void RegisterDeviceAsync_NonZeroHebeStatus_ThrowsHebeExceptionWithCode()
    {
        _handler.Respond("register/jwt", """{"Status":{"Code":108,"Message":"Token wygasł"},"Envelope":null}""");

        var ex = Assert.ThrowsAsync<HebeException>(() => RegisterAsync());

        Assert.Multiple(() =>
        {
            Assert.That(ex!.StatusCode, Is.EqualTo(108));
            Assert.That(ex.Message, Does.Contain("Token wygasł"));
            Assert.That(_handler.Requests, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void RegisterDeviceAsync_HtmlResponse_ThrowsHebeException()
    {
        _handler.Respond("register/hebe", "<!DOCTYPE html><html></html>");

        Assert.ThrowsAsync<HebeException>(() => RegisterAsync());
    }

    [Test]
    public void RegisterDeviceAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.That(() => _service.RegisterDeviceAsync(Ap(Token), cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task ToAccounts_MapsStudentAndSharesDeviceKeys()
    {
        var result = await RegisterAsync();

        var account = result.ToAccounts().Single();
        Assert.Multiple(() =>
        {
            Assert.That(account.PrivatePemKey, Is.EqualTo(result.DeviceKeys.PrivatePemKey));
            Assert.That(account.CertificatePem, Is.EqualTo(result.DeviceKeys.CertificatePem));
            Assert.That(account.Fingerprint, Is.EqualTo(result.DeviceKeys.Fingerprint));
            Assert.That(account.Symbol, Is.EqualTo(Tenant));
            Assert.That(account.RestUrl, Is.EqualTo("https://lekcjaplus.vulcan.net.pl/powiatwulkanowy/000088/api"));
            Assert.That(account.UnitId, Is.EqualTo(1));
            Assert.That(account.UnitName, Is.EqualTo("Szkoła Podstawowa nr 1"));
            Assert.That(account.PupilId, Is.EqualTo(7));
            Assert.That(account.LoginId, Is.EqualTo(70));
            Assert.That(account.FirstName, Is.EqualTo("Jan"));
            Assert.That(account.Surname, Is.EqualTo("Uczeń"));
            Assert.That(account.ClassDisplay, Is.EqualTo("8A"));
        });
    }

    private static bool Verify(RSA publicKey, CapturedRequest request, string signedValue)
    {
        var signature = Regex.Match(request.Headers["Signature"], @"SHA256withRSA\((?<s>[^)]+)\)").Groups["s"].Value;
        return publicKey.VerifyData(Encoding.UTF8.GetBytes(signedValue), Convert.FromBase64String(signature),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    private static string? Thumbprint(CapturedRequest request)
    {
        using var json = JsonDocument.Parse(request.Body);
        return json.RootElement.GetProperty("Envelope").GetProperty("CertificateThumbprint").GetString();
    }

    private static string? SelfIdentifier(CapturedRequest request)
    {
        using var json = JsonDocument.Parse(request.Body);
        return json.RootElement.GetProperty("Envelope").GetProperty("SelfIdentifier").GetString();
    }
}
