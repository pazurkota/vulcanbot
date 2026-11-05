using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using vulcanbot.Dto;
using vulcanbot.Service.Hebe;

namespace vulcanbot.Test.Service;

public class HebeSignerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 10, 30, 0, TimeSpan.Zero);

    private RSA _rsa = null!;
    private DeviceKey _key = null!;

    [SetUp]
    public void Setup()
    {
        _rsa = RSA.Create(2048);
        _key = new DeviceKey(_rsa.ExportPkcs8PrivateKeyPem(), "", "0123456789abcdef0123456789abcdef01234567");
    }

    [TearDown]
    public void TearDown() => _rsa.Dispose();

    [Test]
    public void CanonicalUrl_TakesPathFromApiMobile_EncodedAndLowercased()
    {
        var canonical = HebeSigner.CanonicalUrl(
            "https://lekcjaplus.vulcan.net.pl/powiatwulkanowy/api/mobile/register/hebe?mode=2");

        Assert.That(canonical, Is.EqualTo("api%2fmobile%2fregister%2fhebe%3fmode%3d2"));
    }

    [Test]
    public void CanonicalUrl_WithoutApiMobile_Throws()
    {
        Assert.Throws<ArgumentException>(() => HebeSigner.CanonicalUrl("https://eduvulcan.pl/api/ap"));
    }

    [Test]
    public void Sign_WithBody_SignsCanonicalUrlDigestAndDate()
    {
        const string body = """{"Envelope":{"Tokens":["x"]}}""";
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://lekcjaplus.vulcan.net.pl/powiatwulkanowy/api/mobile/register/jwt");

        HebeSigner.Sign(request, _key, body, Now);

        var digest = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
        Assert.Multiple(() =>
        {
            Assert.That(Header(request, "vCanonicalUrl"), Is.EqualTo("api%2fmobile%2fregister%2fjwt"));
            Assert.That(Header(request, "vDate"), Is.EqualTo("Thu, 05 Nov 2026 10:30:00 GMT"));
            Assert.That(Header(request, "Digest"), Is.EqualTo($"SHA-256={digest}"));
            Assert.That(Header(request, "Signature"), Does.StartWith(
                $"keyId=\"{_key.Fingerprint}\",headers=\"vCanonicalUrl Digest vDate\",algorithm=\"sha256withrsa\","));
            Assert.That(Verify(request, "api%2fmobile%2fregister%2fjwt" + digest + "Thu, 05 Nov 2026 10:30:00 GMT"),
                Is.True);
        });
    }

    [Test]
    public void Sign_WithoutBody_SignsCanonicalUrlAndDateOnly()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://lekcjaplus.vulcan.net.pl/powiatwulkanowy/api/mobile/register/hebe?mode=2");

        HebeSigner.Sign(request, _key, body: null, Now);

        Assert.Multiple(() =>
        {
            Assert.That(request.Headers.Contains("Digest"), Is.False);
            Assert.That(Header(request, "Signature"), Does.Contain("headers=\"vCanonicalUrl vDate\""));
            Assert.That(Verify(request, "api%2fmobile%2fregister%2fhebe%3fmode%3d2Thu, 05 Nov 2026 10:30:00 GMT"),
                Is.True);
        });
    }

    private bool Verify(HttpRequestMessage request, string signedValue)
    {
        var signature = Regex.Match(Header(request, "Signature"), @"SHA256withRSA\((?<s>[^)]+)\)").Groups["s"].Value;
        return _rsa.VerifyData(Encoding.UTF8.GetBytes(signedValue), Convert.FromBase64String(signature),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    private static string Header(HttpRequestMessage request, string name) =>
        string.Join(" ", request.Headers.GetValues(name));
}
