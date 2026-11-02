namespace vulcanbot.Dto.Hebe;

/// <summary>
/// Envelope every Hebe POST body is wrapped in.
/// </summary>
public record HebeRequest<T>(
    string AppName,
    string AppVersion,
    string CertificateId,
    T Envelope,
    string FirebaseToken,
    int API,
    string RequestId,
    long Timestamp,
    string TimestampFormatted
    );
