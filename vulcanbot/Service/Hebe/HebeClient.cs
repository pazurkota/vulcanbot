using System.Globalization;
using System.Text;
using System.Text.Json;
using vulcanbot.Dto;
using vulcanbot.Dto.Hebe;

namespace vulcanbot.Service.Hebe;

/// <summary>
/// Transport for the eduVULCAN mobile API (Hebe CE): request envelope, v* headers, signing
/// and unwrapping of the response envelope.
/// </summary>
public class HebeClient(HttpClient client)
{
    public const string BaseUrl = "https://lekcjaplus.vulcan.net.pl";

    // Identity of the eduVULCAN app. Bump these when the server starts rejecting requests.
    public const string AppName = "DzienniczekPlus 3.0";
    public const string AppVersion = "25.09.24 (G)";
    public const string AppVersionCode = "802";
    public const string UserAgent = "Dart/3.8 (dart:io)";
    public const int ApiVersion = 1;

    public const string DeviceOs = "Android";
    public const string DeviceModel = ".NET 10";

    // Strict: a missing or null field that the DTO says is required fails with the JSON path
    // instead of silently becoming null.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        Converters = { new HebeDateTimeConverter(), new HebeDateOnlyConverter(), new HebeTimeOnlyConverter() }
    };

    public Task<T?> GetAsync<T>(string url, DeviceKey key, int? pupilId, CancellationToken cancellationToken) =>
        SendAsync<T>(HttpMethod.Get, url, key, envelope: null, pupilId, cancellationToken);

    public Task<T?> PostAsync<T>(
        string url,
        DeviceKey key,
        object envelope,
        int? pupilId,
        CancellationToken cancellationToken
        ) =>
        SendAsync<T>(HttpMethod.Post, url, key, envelope, pupilId, cancellationToken);

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string url,
        DeviceKey key,
        object? envelope,
        int? pupilId,
        CancellationToken cancellationToken
        )
    {
        var now = DateTimeOffset.UtcNow;
        var body = envelope is null ? null : BuildBody(envelope, now);

        using var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.Add("vOS", DeviceOs);
        request.Headers.Add("vDeviceModel", DeviceModel);
        request.Headers.Add("vVersionCode", AppVersionCode);
        request.Headers.Add("vAPI", ApiVersion.ToString(CultureInfo.InvariantCulture));
        if (pupilId is not null) request.Headers.Add("vHint", pupilId.Value.ToString(CultureInfo.InvariantCulture));
        HebeSigner.Sign(request, key, body, now);

        using var response = await client.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode) throw new HebeException($"{method} {url} failed. " +
                                                                   $"Status: {response.StatusCode}. " +
                                                                   $"Message: {json}");

        HebeResponse<T>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<HebeResponse<T>>(json, JsonOptions);
        }
        catch (JsonException e)
        {
            throw new HebeException($"{method} {url} returned an unexpected response: {e.Message}", innerException: e);
        }

        if (parsed?.Status is null) throw new HebeException($"{method} {url} returned a response without Status.");
        if (parsed.Status.Code != 0) throw new HebeException($"{method} {url} failed. " +
                                                             $"Hebe status: {parsed.Status.Code}. " +
                                                             $"Message: {parsed.Status.Message}",
                                                             parsed.Status.Code);

        return parsed.Envelope;
    }

    private static string BuildBody(object envelope, DateTimeOffset now) => JsonSerializer.Serialize(new
    {
        AppName,
        AppVersion,
        NotificationToken = "",
        API = ApiVersion,
        RequestId = Guid.NewGuid(),
        Timestamp = now.ToUnixTimeSeconds(),
        TimestampFormatted = now.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        Envelope = envelope
    });
}
