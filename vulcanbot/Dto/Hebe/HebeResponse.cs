namespace vulcanbot.Dto.Hebe;

/// <summary>
/// Envelope every Hebe (UONET+ mobile API) response is wrapped in.
/// </summary>
public record HebeResponse<T>(
    T? Envelope,
    HebeStatus Status,
    string? RequestId,
    long Timestamp,
    string? TimestampFormatted,
    string? EnvelopeType,
    string? InResponseTo
    );

public record HebeStatus(
    int Code,
    string Message
    );
