using System.Security.Cryptography;
using System.Text;
using vulcanbot.Service.Hebe;

namespace vulcanbot.Test.Service.Hebe;

/// <summary>
/// Expected values were produced by the reference Python signer (wulkanowy/uonet-request-signer, hebe)
/// for the same key, URL, body and timestamp. RSA PKCS#1 v1.5 signatures are deterministic,
/// so the output must match byte for byte.
/// </summary>
public class HebeSignerTests
{
    private const string Fingerprint = "0123456789abcdef0123456789abcdef01234567";
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 14, 43, 13, TimeSpan.Zero);

    private const string PrivateKeyPem = """
-----BEGIN PRIVATE KEY-----
MIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQC4jTgjMcVMcK63
nBPG9UtHziMWfrmePeS1FsXltQmd40r5HVL3Q/E/YnVge87A8/mIKI4b1+zaJ2ns
BOZUAmKtFsYnKx1TFH28YWqxPSCRsyxQBtRmXs7D9kRUTi4z/wvZbIyUlnuvXCzM
x0+CnSVuhG5V9eHvHzzwd8cr5Iw6FlSVpic6MYturvM39Qq549aI9EZ5KuEI577x
7lEuXXXlrESKUSsSPuXNYrQdG+0dWDLhysbP74PFS5P1j7glpiiOcIvQ1BnO/ywh
WxOqURKDapb7SQ/ZI3CruVs0dlOkzz7BGu3BTOUaDbuwMi/10HNSxjDrw9UsLGTh
RZ/OkwYtAgMBAAECggEAKLSFOEYHBWgpMThMqGp8EzdlR9XlrQBrzIFcXuECYP+8
DpwoK3+h22AlEYXdQXPOivie94c1p5xf4X3nK4pnJ1RJmmQJEMUJQp6eAL/BykEh
ufj3qiipJFHSGmIwEKnlfCxUCYXze+cxvm0LnXx5wBmXmIAmOC7EqylQFd9QCpN3
ueeA3cWk42c8zkAk5gXD5qndTwiXfLeJNmHa5R6gl/xRmEQuTMp0vqX7oIHDu6FF
IAKZvb9mWeqL7swnMEPEmjaaCv73eaY6xjU+JZXr3MujLLgHEeNE0Zfv+2BefPa0
3Agnnd0nWDGeew9Wz19aFuEwekxyLgq1XI5iQNJhIQKBgQD1L8HWQtrSRvhi7rEN
/CbKHKNhe+SO3vjeBlF/6Aa0HrCovSQoR/la6rhZH4MBNV1EJBwDL2w2TaM4yTdg
4Ud7OWoedaxW4+Q9Y5sE8EJY5OhsU9fi6NgQC4l+TKzoN2FFJLXE4e73/a4fiNZs
MhsrTF766r3siTwsBVHmS8T3WQKBgQDAsN9cizEPUGinLT/AfQONzkZgCoqVlLB8
rrTKxo0PsOZdJn30Mo2hiOpRzt7/CzkrmxBwoZKw4vKL7FJIxdTpjALvCWxBf+Al
KB3zS/ziDnID+AUGA2XOYLxh9MMZeg2wK+9/72qV1EIp7dbRKJCBxg6r8eHcMgC4
huCHvej+9QKBgAVeXVUus5HlIe0wosnWmpbUV7N7vf5u3W4nH0O4teTdwyp+sbr8
EHawKwcwjHLLU1Z5n+9l622o94j3xvGlHSaSlENEeliQtt+tPai3BgziPkCY+WIr
jQeU6RbN4P9YrxWPfzdnZw8hC93+5gsKcdPZ4T6HQ09Zleq01x0+zXpJAoGBALdD
AQxJLZ29AeuNs4Zk345s0q3hGLDuu3O5zSTV9a8otc9+Hmiz89PgHcs0jw3OJM9+
KQKWCKfHEJtXZWLlFHwSt2+UbKtW7V0pbGLKsHZm+rUfm9d+P8MqacBYQ33lgUM+
ZggtMhfRyiOKmrZOeDGUpruNzxvXSqw1SzKOwhxJAoGAPcA51qOno9K0VmlCI3Qt
c+tDXEFQBytilGBtzrM6qZSWqZrfjRH+mhtjKOLcEnWms5WOdJsR7TfFgrQzZdZh
PHOovVOni17FLOtPY7cjeFp2uxctUuC7n7Ie5zryh5g1h5URyuUao6rBOxGZ12tE
zq9DZPnZY1BI/HoBCnlcpsI=
-----END PRIVATE KEY-----
""";

    private const string PostUrl = "https://lekcjaplus.vulcan.net.pl/powiatwulkanowy/api/mobile/register/new";
    private const string PostBody = """{"Envelope":{"PIN":"123456"}}""";

    private const string GetUrl =
        "https://lekcjaplus.vulcan.net.pl/powiatwulkanowy/123456/api/mobile/exam/byPupil?unitId=2&pupilId=111&lastSyncDate=1970-01-01 01:00:00&pageSize=500";

    [Test]
    public void Sign_Post_MatchesReferenceImplementation()
    {
        var result = HebeSigner.Sign(Fingerprint, PrivateKeyPem, PostUrl, PostBody, Now);

        Assert.Multiple(() =>
        {
            Assert.That(result.Digest, Is.EqualTo("SHA-256=haYUzkTbQPk27VraNFoCIAD1IQ01cxwfPcgrsmCItYk="));
            Assert.That(result.CanonicalUrl, Is.EqualTo("api%2fmobile%2fregister%2fnew"));
            Assert.That(result.Date, Is.EqualTo("Mon, 02 Nov 2026 14:43:13 GMT"));
            Assert.That(result.Signature, Is.EqualTo(
                """keyId="0123456789abcdef0123456789abcdef01234567",headers="vCanonicalUrl Digest vDate",algorithm="sha256withrsa",signature=Base64(SHA256withRSA(LxP+4ViqdLammktlKy3590EIgTf4hCbY6xzEztxLXTQw+lrsJSDEvPILaUV+jPpupako+ivfwc1NQhJ8pxD9V8yUi7QcSA3hmAR8swRk00CfQf+ngQm8pRv1Ue5b8FYjxg9Q4BbJ2GWs9y8ktoQTXlAfaD2Dp14pDyqOwcOR7KBkgYLiXd0suf+iH2Rre8fivIiwjlnn17Aw2336Vz95Ic1IkVCm5111fQE16d6luXuxG21eANQ/uEulg4qmZaoPjT9mlb/fYvvRZsxNYYzxMyPrXA9rvLB4FJFpTg5J/TIQEKMvp6bzSpRg7adDaMbITQG50y2ZWCUJftXqdgeL8g==))"""));
        });
    }

    [Test]
    public void Sign_GetWithQuery_MatchesReferenceImplementation()
    {
        var result = HebeSigner.Sign(Fingerprint, PrivateKeyPem, GetUrl, null, Now);

        Assert.Multiple(() =>
        {
            Assert.That(result.Digest, Is.Null);
            Assert.That(result.CanonicalUrl, Is.EqualTo("api%2fmobile%2fexam%2fbypupil%3funitid%3d2%26pupilid%3d111%26lastsyncdate%3d1970-01-01%2001%3a00%3a00%26pagesize%3d500"));
            Assert.That(result.Signature, Is.EqualTo(
                """keyId="0123456789abcdef0123456789abcdef01234567",headers="vCanonicalUrl vDate",algorithm="sha256withrsa",signature=Base64(SHA256withRSA(L55cSDW+sqqHXTQ3mknVTBsBDgTWdolg2P6ts+KH2R4zfi7qZsshERGu9sLCNRHs50bufnNIvmf2A5yRWeR9+du0MTQXdmZGFkKFTb66G+0d3TB1Me/re08vhfTY06CTHgovUTnXxNePhoC2M5JGdG75VM/96SKbj8fHb8LXRHeZsqu7jSgQu73ANBa9YY1aCQVu8I70IXkfkJo9WLFbJd0VJeUQ6VT9LhrvIbcPwVwK5pN2eNucXxwuQ/wyk53n4jON+sf0ZZmiMVJiZNTR3H9L+UHNYn3tM6gXz7uWLp+5U7PIt1bufeXQOmVVUbpi4s63SRecEP9nuYGm+Ftxag==))"""));
        });
    }

    [Test]
    public void Sign_SignatureVerifiesWithPublicKey()
    {
        var result = HebeSigner.Sign(Fingerprint, PrivateKeyPem, PostUrl, PostBody, Now);
        var signature = result.Signature.Split("SHA256withRSA(")[1].TrimEnd(')');
        var signedData = result.CanonicalUrl + result.Digest!["SHA-256=".Length..] + result.Date;

        using var rsa = RSA.Create();
        rsa.ImportFromPem(PrivateKeyPem);

        Assert.That(rsa.VerifyData(Encoding.UTF8.GetBytes(signedData), Convert.FromBase64String(signature),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), Is.True);
    }

    /// <summary>
    /// Test vector from wulkanowy/uonet-request-signer hebe-jvm (SignerTest.kt), an independent implementation.
    /// The JVM signer formats the Signature header slightly differently, so only the digest, canonical URL,
    /// date and the raw RSA signature are compared.
    /// </summary>
    [TestCase("{}", "vCanonicalUrl Digest vDate", "SHA-256=RBNvo1WzZ4oRRq0W9+hknpT7T8If536DEMBg9hyq/4o=",
        "JLdhYWv05+f7KTstWsCgt0rU2QQpA+jZM6VaVKFYe0Q4hrKKVq5/ZPB3ttBJ0RwD+MGX2mePkYm3BiLdCOqoZfAyylpHCnnQ4lbFOX45sMogQQoFbhmIQF1ZwVxRKrn/lbrd+VsLsInXWn74CMNIOz55p/WrwV3J+w5g1FpYSRM/LVzQMXvcRRXx6WSmfo/qd1H6TH33EVU+fPTw3lhGpAPDl+clZyUDyfWrmxaRvJ/ag2LNdtGVNXDQfyMO7diOJQtqUnWsJoMh5iNFApd9nHgB1Fkmb62aDwGDGtDz65IA7ArdjNW56IXeogQyp+Vv/icap3/ujdQl5zjXssL4Sg==")]
    [TestCase(null, "vCanonicalUrl vDate", null,
        "HF8AbXOjH8CBu+xfviwSXz46GwuRDQL47Bb6rIumdEZ0l6sUF7oMakhFVPBc3G43sNSSIZsxTgdBu0GB+66OHj1ce6/E5WTUi01/chs22mqTFfGbkqQ2eebmmSciW1qGiYy/0IfwidTVJXJa4EYWjsM3538WMvaeoOuqAKOwrOGgQG0qmuR8ZUKIit3R0BbQTTBlV5jX2MVTmTTo2Knx5q9Hz5PTPdyzuKYGD5mXRhx42WK6EzSzxzbdY8+s3esCCEEAeXQS+Bps0nF+h8xFXwc+QHgPjlGVX2HE/EEw+Kp6AW3ub8QjvbvO+mkNU/JaFQBOvRvCy0heMjOTt66QxA==")]
    public void Sign_MatchesWulkanowyJvmTestVector(string? body, string headerNames, string? digest, string rsaSignature)
    {
        const string keyId = "7eba57e1ddba1c249d097a9ff1c9ccdd45351a6a";
        const string pem = "-----BEGIN PRIVATE KEY-----\n" + JvmPrivateKey + "\n-----END PRIVATE KEY-----";

        var result = HebeSigner.Sign(keyId, pem, "/powiatwulkanowy/123456/api/mobile/register/hebe?lastSyncDate=null", body,
            DateTimeOffset.Parse("2021-02-18T16:36:53Z"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Digest, Is.EqualTo(digest));
            Assert.That(result.CanonicalUrl, Is.EqualTo("api%2fmobile%2fregister%2fhebe%3flastsyncdate%3dnull"));
            Assert.That(result.Date, Is.EqualTo("Thu, 18 Feb 2021 16:36:53 GMT"));
            Assert.That(result.Signature, Is.EqualTo(
                $"keyId=\"{keyId}\",headers=\"{headerNames}\",algorithm=\"sha256withrsa\",signature=Base64(SHA256withRSA({rsaSignature}))"));
        });
    }

    private const string JvmPrivateKey =
        "MIIEvgIBADANBgkqhkiG9w0BAQEFAASCBKgwggSkAgEAAoIBAQDCbF5Tt176EpB4cX5U+PZE0XytjJ9ABDZFaBFDkaexbkuNeuLOaARjQEOlUoBmpZQXxAF8HlYqeTvPiTcnSfQIS6EdqpICuQNdwvy6CHFAe2imkbbB0aHPsGep6zH8ZxHbssazkTCnGy0j2ZtGT2/iy1GEvc/p2bOkCVcR1H1GqFp+/XpfaMwi2SRCwc67K8Fu8TjSDKNvcRT9nuenGoPA1CWoOiOCxhQA6gnB8LULPel6TVDxeBVdYor/z2GxFe/m0pa7XAKzveuUDhH8k8NlNG65MjvZhgy9iFs+eBUq7lCZ0nuIsDzjnUrLSl4ciYKj9d94qrUyF8L8D9Rl+0WlAgMBAAECggEAQ6jg3rNmyxIg0rl0ZG/LjEF26RKR7P5KQLcpouESga3HfzHvsjMCq+OWZvciFhazRd4BQkdwZxGPnfa7ieGzmhtvs1pDu8zU/hE4UClV+EG6NpVpC2Q/sn5KZRijaZoY3eMGQUFatBzCBcLZxYspfbyR3ucLbu9DE+foNB1Fh4u9RCDj3bClTsqPcNBIaLMpYr3f/bM1fFbS9LrJ7AXZQtGg/2MH58WsvV67SiYAQqGCzld/Jp74gmod4Ii0w2XWZ7OeixdF2xr1j7TK0dUUlrrOrb1cgOWSOEXyy3RX/iF7R8uuLXiRfo1URh6VNPoOtrC6fHCrCp1iRBo08qOk4QKBgQDxqLrWA7gKcTr2yQeGfETXOAYi0xqbNj5A9eVC0XngxnFuwWc5zyg3Ps3c0UK2qTSSFv4SoeEHQM+U0+9LjYzIRSUH7zy4zBrBlLtTQCysSuuZ9QfgO55b3/QEYkyx6Hz/z/gg53jKHjsUKIftGMwJ6C1M2svbBNYCsWrUuYcsbQKBgQDN9gkVDABIeWUtKDHyB5HGcVbsg7Ji7GhMjdFA0GB+9kR0doKNctrzxKn65BI2uTWg+mxaw5V+UeJOIaeFsv2uClYJYn1F55VT7NIx3CLFv6zFRSiMSKz2W+NkwGjQqR7D3DeEyalpjeQeMdpHZg27LMbdVkzy/cK8EM9ZQlRLGQKBgQCpB2wn5dIE+85Sb6pj1ugP4Y/pK9+gUQCaT2RcqEingCY3Ye/h75QhkDxOB9CyEwhCZvKv9aqAeES5xMPMBOZD7plIQ34lhB3y6SVdxbV5ja3dshYgMZNCkBMOPfOHPSaxh7X2zfEe7qZEI1Vv8bhF9bA54ZBVUbyfhZlD0cFKwQKBgQC9BnXHb0BDQ8br7twH+ZJ8wkC4yRXLXJVMzUujZJtrarHhAXNIRoVU/MXUkcV1m/3wRGV119M4IAbHFnQdbO0N8kaMTmwS4DxYzh0LzbHMM+JpGtPgDENRx3unWD/aYZzuvQnnQP3O9n7Kh46BwNQRWUMamL3+tY8n83WZwhqC4QKBgBTUzHy0sEEZ3hYgwU2ygbzC0vPladw2KqtKy+0LdHtx5pqE4/pvhVMpRRTNBDiAvb5lZmMB/B3CzoiMQOwczuus8Xsx7bEci28DzQ+g2zt0/bC2Xl+992Ass5PP5NtOrP/9QiTNgoFSCrVnZnNzQqpjCrFsjfOD2fiuFLCD6zi6";

    [Test]
    public void FormatDate_ConvertsToGmt()
    {
        var local = new DateTimeOffset(2026, 11, 2, 15, 43, 13, TimeSpan.FromHours(1));

        Assert.That(HebeSigner.FormatDate(local), Is.EqualTo("Mon, 02 Nov 2026 14:43:13 GMT"));
    }

    [Test]
    public void GetCanonicalUrl_UrlWithoutApiMobile_Throws()
    {
        Assert.Throws<ArgumentException>(() => HebeSigner.GetCanonicalUrl("https://example.com/foo"));
    }

    [TestCase(null)]
    [TestCase("")]
    public void GetDigest_EmptyBody_ReturnsNull(string? body)
    {
        Assert.That(HebeSigner.GetDigest(body), Is.Null);
    }
}
