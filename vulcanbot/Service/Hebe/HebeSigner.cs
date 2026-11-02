using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace vulcanbot.Service.Hebe;

public record HebeSignature(
    string? Digest,
    string CanonicalUrl,
    string Date,
    string Signature
    );

/// <summary>
/// Port of wulkanowy/uonet-request-signer (hebe): every Hebe request is signed with the device's RSA key.
/// Signed string = vCanonicalUrl + raw body digest (POST only) + vDate.
/// </summary>
public static partial class HebeSigner
{
    [GeneratedRegex("(api/mobile/.+)")]
    private static partial Regex ApiPathRegex();

    public static HebeSignature Sign(string fingerprint, string privatePemKey, string fullUrl, string? body, DateTimeOffset now)
    {
        var canonicalUrl = GetCanonicalUrl(fullUrl);
        var digest = GetDigest(body);
        var date = FormatDate(now);

        var headerNames = digest is null ? "vCanonicalUrl vDate" : "vCanonicalUrl Digest vDate";
        var signedData = canonicalUrl + digest + date;

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privatePemKey);
        var signature = Convert.ToBase64String(
            rsa.SignData(Encoding.UTF8.GetBytes(signedData), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        return new HebeSignature(
            digest is null ? null : $"SHA-256={digest}",
            canonicalUrl,
            date,
            $"keyId=\"{fingerprint}\",headers=\"{headerNames}\",algorithm=\"sha256withrsa\",signature=Base64(SHA256withRSA({signature}))"
            );
    }

    /// <summary>The part of the URL starting at <c>api/mobile/</c> (query included), percent-encoded and lowercased.</summary>
    public static string GetCanonicalUrl(string fullUrl)
    {
        var match = ApiPathRegex().Match(fullUrl);
        if (!match.Success)
            throw new ArgumentException($"URL '{fullUrl}' does not contain 'api/mobile/'.", nameof(fullUrl));

        return Uri.EscapeDataString(match.Groups[1].Value).ToLowerInvariant();
    }

    public static string? GetDigest(string? body) =>
        string.IsNullOrEmpty(body) ? null : Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(body)));

    /// <summary>RFC 1123 date in GMT, e.g. "Mon, 02 Nov 2026 14:43:13 GMT".</summary>
    public static string FormatDate(DateTimeOffset now) =>
        now.ToUniversalTime().ToString("r", CultureInfo.InvariantCulture);
}
