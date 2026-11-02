using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using vulcanbot.Dto;
using vulcanbot.Dto.Hebe;

namespace vulcanbot.Service.Hebe;

/// <summary>
/// Sends signed requests to the Hebe API and unwraps the response envelope,
/// turning a non-zero <c>Status.Code</c> into a <see cref="VulcanApiException"/>.
/// </summary>
public class HebeApiClient(HttpClient client, TimeProvider timeProvider)
{
    public Task<TResponse> PostAsync<TEnvelope, TResponse>(
        string fullUrl,
        TEnvelope envelope,
        DeviceKey deviceKey,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var request = new HebeRequest<TEnvelope>(
            AppName: HebeConstants.AppName,
            AppVersion: HebeConstants.AppVersion,
            CertificateId: deviceKey.Fingerprint,
            Envelope: envelope,
            FirebaseToken: "",
            API: 1,
            RequestId: Guid.NewGuid().ToString(),
            Timestamp: now.ToUnixTimeMilliseconds(),
            TimestampFormatted: now.ToString("yyyy-MM-dd HH:mm:ss")
            );

        return SendAsync<TResponse>(HttpMethod.Post, fullUrl, JsonSerializer.Serialize(request), deviceKey, now, cancellationToken);
    }

    public Task<TResponse> GetAsync<TResponse>(string fullUrl, DeviceKey deviceKey, CancellationToken cancellationToken) =>
        SendAsync<TResponse>(HttpMethod.Get, fullUrl, null, deviceKey, timeProvider.GetUtcNow(), cancellationToken);

    private async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method,
        string fullUrl,
        string? body,
        DeviceKey deviceKey,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var signature = HebeSigner.Sign(deviceKey.Fingerprint, deviceKey.PrivatePemKey, fullUrl, body, now);

        using var request = new HttpRequestMessage(method, fullUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", HebeConstants.UserAgent);
        request.Headers.TryAddWithoutValidation("vOS", HebeConstants.Os);
        request.Headers.TryAddWithoutValidation("vDeviceModel", HebeConstants.DeviceModel);
        request.Headers.TryAddWithoutValidation("vAPI", "1");
        request.Headers.TryAddWithoutValidation("vDate", signature.Date);
        request.Headers.TryAddWithoutValidation("vCanonicalUrl", signature.CanonicalUrl);
        request.Headers.TryAddWithoutValidation("Signature", signature.Signature);

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Headers.TryAddWithoutValidation("Digest", signature.Digest);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        HebeResponse<TResponse>? parsed = null;
        try
        {
            parsed = JsonSerializer.Deserialize<HebeResponse<TResponse>>(json);
        }
        catch (JsonException) when (!response.IsSuccessStatusCode)
        {
            // fall through to the HTTP error below
        }
        catch (JsonException e)
        {
            throw new VulcanApiException(VulcanErrorKind.InvalidResponse, (int)response.StatusCode,
                $"Vulcan returned a response that is not a valid Hebe envelope: {Truncate(json)}", e);
        }

        if (parsed?.Status is { } status)
        {
            if (status.Code != 0) throw VulcanApiException.FromStatus(status.Code, status.Message);
            if (parsed.Envelope is null)
                throw new VulcanApiException(VulcanErrorKind.InvalidResponse, status.Code, "Vulcan response has no envelope.");
            return parsed.Envelope;
        }

        // Vulcan answers an unknown symbol with HTTP 500 and a .NET ArgumentException in the body.
        if ((int)response.StatusCode == 500 && json.Contains("ArgumentException"))
            throw new VulcanApiException(VulcanErrorKind.InvalidSymbol, 500, "Invalid symbol.");

        throw new VulcanApiException(
            response.IsSuccessStatusCode ? VulcanErrorKind.InvalidResponse : VulcanErrorKind.HttpError,
            (int)response.StatusCode,
            $"Unexpected response from Vulcan (HTTP {(int)response.StatusCode}): {Truncate(json)}");
    }

    private static string Truncate(string s) => s.Length <= 500 ? s : s[..500] + "…";
}
