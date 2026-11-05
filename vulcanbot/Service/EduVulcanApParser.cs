using System.Buffers.Text;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using vulcanbot.Dto;

namespace vulcanbot.Service;

/// <summary>
/// Reads the JWT tokens pasted from https://eduvulcan.pl/api/ap and groups them by tenant (school symbol).
/// </summary>
public static partial class EduVulcanApParser
{
    [GeneratedRegex("""value\s*=\s*(?<q>["'])(?<v>.*?)\k<q>""", RegexOptions.Singleline)]
    private static partial Regex InputValue();

    /// <summary>
    /// Accepts the raw JSON (the value of the <c>#ap</c> input) or the whole <c>&lt;input&gt;</c> element
    /// copied from the page source.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseTokensByTenant(string input)
    {
        var ap = Parse(input);

        if (!ap.Success) throw new InvalidOperationException($"eduVULCAN returned Success=false: {ap.ErrorMessage}");
        if (!ap.IsConsentAccepted) throw new InvalidOperationException(
            "Consents are not accepted — accept them on eduvulcan.pl and copy the tokens again.");
        if (ap.Tokens is not { Count: > 0 }) throw new InvalidOperationException(
            "No tokens — the eduVULCAN account has no students linked.");

        return ap.Tokens
            .GroupBy(GetTenant)
            .ToDictionary(g => g.Key, IReadOnlyList<string> (g) => g.ToList());
    }

    private static EduVulcanAp Parse(string input)
    {
        var text = input.Trim();
        if (text.StartsWith('<'))
        {
            var match = InputValue().Match(text);
            if (!match.Success) throw new FormatException("Pasted HTML does not contain a value attribute.");
            text = match.Groups["v"].Value;
        }

        text = WebUtility.HtmlDecode(text);

        try
        {
            return JsonSerializer.Deserialize<EduVulcanAp>(text)
                   ?? throw new FormatException("Pasted text is not the JSON from eduvulcan.pl/api/ap.");
        }
        catch (JsonException e)
        {
            throw new FormatException("Pasted text is not the JSON from eduvulcan.pl/api/ap.", e);
        }
    }

    private static string GetTenant(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length != 3) throw new FormatException("Token is not a JWT.");

        using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
        return payload.RootElement.TryGetProperty("tenant", out var tenant) && tenant.GetString() is { Length: > 0 } value
            ? value
            : throw new FormatException("JWT payload has no tenant.");
    }
}
