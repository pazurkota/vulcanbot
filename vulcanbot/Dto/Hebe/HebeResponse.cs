namespace vulcanbot.Dto.Hebe;

/// <summary>
/// Envelope every Hebe (UONET+ mobile API) response is wrapped in.
/// </summary>
public record HebeResponse<T>(
    HebeStatus Status,
    T? Envelope = default,
    string? RequestId = null,
    long Timestamp = 0,
    string? TimestampFormatted = null,
    string? EnvelopeType = null,
    string? InResponseTo = null
    );

public record HebeStatus(
    int Code,
    string? Message
    );
