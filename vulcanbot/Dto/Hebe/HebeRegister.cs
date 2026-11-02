using System.Text.Json.Serialization;

namespace vulcanbot.Dto.Hebe;

/// <summary>Body of <c>api/mobile/register/new</c>.</summary>
public record HebeRegisterRequest(
    string OS,
    string DeviceModel,
    string Certificate,
    string CertificateType,
    string CertificateThumbprint,
    string PIN,
    string SecurityToken,
    string SelfIdentifier
    );

/// <summary>Envelope returned by <c>api/mobile/register/new</c>.</summary>
public record HebeRegisterResponse(
    int LoginId,
    string UserLogin,
    string UserName,
    [property: JsonPropertyName("RestURL")] string RestUrl
    );
