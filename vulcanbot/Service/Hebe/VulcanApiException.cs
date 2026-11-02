namespace vulcanbot.Service.Hebe;

public enum VulcanErrorKind
{
    Unknown,
    HttpError,
    InvalidResponse,
    InvalidSignature,
    UnauthorizedCertificate,
    InvalidToken,
    InvalidPin,
    ExpiredToken,
    InvalidSymbol
}

public class VulcanApiException(VulcanErrorKind kind, int? statusCode, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public VulcanErrorKind Kind { get; } = kind;

    /// <summary>Hebe <c>Status.Code</c>, or HTTP status code for <see cref="VulcanErrorKind.HttpError"/>.</summary>
    public int? StatusCode { get; } = statusCode;

    public static VulcanApiException FromStatus(int code, string? message)
    {
        var kind = code switch
        {
            100 when message?.Contains(": ") == true => VulcanErrorKind.InvalidSignature,
            108 => VulcanErrorKind.UnauthorizedCertificate,
            200 => VulcanErrorKind.InvalidToken,
            203 => VulcanErrorKind.InvalidPin,
            204 => VulcanErrorKind.ExpiredToken,
            -1 => VulcanErrorKind.InvalidSymbol,
            _ => VulcanErrorKind.Unknown
        };

        var description = kind switch
        {
            VulcanErrorKind.InvalidSignature => "Invalid request signature",
            VulcanErrorKind.UnauthorizedCertificate => "Device certificate is not authorized (device removed or not registered)",
            VulcanErrorKind.InvalidToken => "Invalid token",
            VulcanErrorKind.InvalidPin => "Invalid PIN",
            VulcanErrorKind.ExpiredToken => "Token has expired",
            VulcanErrorKind.InvalidSymbol => "Invalid symbol",
            _ => "Vulcan API error"
        };

        return new VulcanApiException(kind, code, $"{description} (code {code}): {message}");
    }
}
