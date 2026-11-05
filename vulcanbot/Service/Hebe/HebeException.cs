namespace vulcanbot.Service.Hebe;

/// <summary>
/// Hebe request failed — HTTP error, non-JSON response or non-zero <c>Status.Code</c> in the envelope.
/// </summary>
public class HebeException(string message, int? statusCode = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>
    /// <c>Status.Code</c> from the Hebe response, when the server returned one.
    /// </summary>
    public int? StatusCode { get; } = statusCode;
}
