using System.Net;

namespace vulcanbot.Test.Helpers;

public sealed record CapturedRequest(
    HttpMethod Method,
    Uri? Uri,
    IReadOnlyDictionary<string, string> Headers,
    string? ContentType,
    string Body);

/// <summary>
/// Returns a canned response per URL (or a default one) and records every request.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (HttpStatusCode Status, string Body, string ContentType)> _responses = new();

    public HttpStatusCode DefaultStatus { get; set; } = HttpStatusCode.OK;
    public string DefaultBody { get; set; } = "";
    public List<CapturedRequest> Requests { get; } = [];

    public void RespondTo(string url, string body, HttpStatusCode status = HttpStatusCode.OK, string contentType = "application/json") =>
        _responses[url] = (status, body, contentType);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The request is disposed by the caller after SendAsync returns, so capture everything now.
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(" ", h.Value));
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new CapturedRequest(request.Method, request.RequestUri, headers,
            request.Content?.Headers.ContentType?.ToString(), body));

        var (status, responseBody, contentType) = _responses.TryGetValue(request.RequestUri!.ToString(), out var r)
            ? r
            : (DefaultStatus, DefaultBody, "application/json");

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(responseBody, System.Text.Encoding.UTF8, contentType)
        };
    }
}

public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
