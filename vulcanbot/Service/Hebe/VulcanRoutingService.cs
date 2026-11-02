namespace vulcanbot.Service.Hebe;

/// <summary>
/// Resolves the Hebe base URL from the first 3 characters of a registration token
/// using Vulcan's public routing table.
/// </summary>
public class VulcanRoutingService(HttpClient client)
{
    public const string RoutingRulesUrl = "https://komponenty.vulcan.net.pl/UonetPlusMobile/RoutingRules.txt";

    private IReadOnlyDictionary<string, string>? _rules;

    public async Task<string> GetBaseUrlAsync(string token, CancellationToken cancellationToken)
    {
        if (token.Length < 3)
            throw new VulcanApiException(VulcanErrorKind.InvalidToken, null, "Token is too short.");

        var prefix = token[..3].ToUpperInvariant();
        _rules ??= await LoadRulesAsync(cancellationToken);

        return _rules.TryGetValue(prefix, out var url)
            ? url
            : throw new VulcanApiException(VulcanErrorKind.InvalidToken, null, $"Unknown token prefix '{prefix}'.");
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadRulesAsync(CancellationToken cancellationToken)
    {
        var text = await client.GetStringAsync(RoutingRulesUrl, cancellationToken);
        return ParseRules(text);
    }

    /// <summary>Parses lines like <c>3S1,https://lekcjaplus.vulcan.net.pl</c>.</summary>
    public static IReadOnlyDictionary<string, string> ParseRules(string text) =>
        text.Split((char[])['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(',', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0)
            .GroupBy(parts => parts[0].ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First()[1].TrimEnd('/'));
}
