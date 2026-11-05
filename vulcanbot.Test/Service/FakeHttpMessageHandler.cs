using System.Net;

namespace vulcanbot.Test.Service;

public sealed record CapturedRequest(
    HttpMethod Method,
    Uri? Uri,
    IReadOnlyDictionary<string, string> Headers,
    string? ContentType,
    string Body);

/// <summary>
/// Records every request and answers with the response registered for the first matching URL fragment.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly List<(string UrlFragment, HttpStatusCode Status, string Body)> _responses = [];

    public List<CapturedRequest> Requests { get; } = [];

    public void Respond(string urlFragment, string body, HttpStatusCode status = HttpStatusCode.OK) =>
        _responses.Insert(0, (urlFragment, status, body));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Request is disposed by the caller after SendAsync returns, so capture everything now.
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(" ", h.Value));
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new CapturedRequest(
            request.Method,
            request.RequestUri,
            headers,
            request.Content?.Headers.ContentType?.MediaType,
            body));

        var url = request.RequestUri!.OriginalString;
        var (_, status, responseBody) = _responses.FirstOrDefault(r => url.Contains(r.UrlFragment));

        return new HttpResponseMessage(responseBody is null ? HttpStatusCode.NotFound : status)
        {
            Content = new StringContent(responseBody ?? "")
        };
    }
}
