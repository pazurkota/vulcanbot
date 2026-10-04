namespace vulcanbot.Dto;

public record DeviceKey(
    string PrivatePemKey,
    string CertificatePem,
    string Fingerprint
    );