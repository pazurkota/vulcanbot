using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using vulcanbot.Dto;

namespace vulcanbot.Service.Hebe;

/// <summary>
/// Signs Hebe requests the way the eduVULCAN mobile app does:
/// RSA PKCS#1 v1.5 + SHA-256 over the concatenated values of vCanonicalUrl [+ Digest] + vDate.
/// </summary>
public static partial class HebeSigner
{
    [GeneratedRegex("api/mobile/.+")]
    private static partial Regex MobilePath();

    /// <summary>
    /// Part of the URL starting at <c>api/mobile/</c> (query included), URL-encoded and lowercased.
    /// </summary>
    public static string CanonicalUrl(string url)
    {
        var match = MobilePath().Match(url);
        if (!match.Success) throw new ArgumentException($"URL does not contain an api/mobile/ path: {url}", nameof(url));

        return Uri.EscapeDataString(match.Value).ToLowerInvariant();
    }

    public static string Digest(string body) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(body)));

    /// <summary>
    /// Adds vCanonicalUrl, vDate, Digest (when there is a body) and Signature headers to the request.
    /// <paramref name="body"/> must be exactly the string sent as the request content.
    /// </summary>
    public static void Sign(HttpRequestMessage request, DeviceKey key, string? body, DateTimeOffset now)
    {
        var canonicalUrl = CanonicalUrl(request.RequestUri!.OriginalString);
        var vDate = now.ToUniversalTime().ToString("r", CultureInfo.InvariantCulture);
        var digest = body is null ? null : Digest(body);
        var signedHeaders = digest is null ? "vCanonicalUrl vDate" : "vCanonicalUrl Digest vDate";

        using var rsa = RSA.Create();
        rsa.ImportFromPem(key.PrivatePemKey);
        var signature = Convert.ToBase64String(rsa.SignData(
            Encoding.UTF8.GetBytes(canonicalUrl + digest + vDate),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1));

        request.Headers.TryAddWithoutValidation("vCanonicalUrl", canonicalUrl);
        request.Headers.TryAddWithoutValidation("vDate", vDate);
        if (digest is not null) request.Headers.TryAddWithoutValidation("Digest", $"SHA-256={digest}");
        request.Headers.TryAddWithoutValidation("Signature",
            $"keyId=\"{key.Fingerprint}\",headers=\"{signedHeaders}\",algorithm=\"sha256withrsa\"," +
            $"signature=Base64(SHA256withRSA({signature}))");
    }
}
