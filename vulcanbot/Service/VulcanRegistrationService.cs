using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using vulcanbot.Dto;
using vulcanbot.Dto.Hebe;
using vulcanbot.Service.Hebe;

namespace vulcanbot.Service;

public class VulcanRegistrationService(HebeApiClient apiClient, VulcanRoutingService routing)
{
    public const string RegisterPath = "api/mobile/register/new";

    /// <summary>
    /// Registers a new device on the user's Vulcan account using the token, symbol and PIN
    /// shown in "Dostęp mobilny" in the Vulcan web journal.
    /// </summary>
    /// <exception cref="VulcanApiException">Vulcan rejected the request (wrong PIN, expired token, …).</exception>
    public async Task<RegistrationResult> RegisterDeviceAsync(
        string token,
        string pin,
        string symbol,
        CancellationToken cancellationToken
        )
    {
        token = token.Trim().ToUpperInvariant();
        symbol = symbol.Trim().ToLowerInvariant();
        pin = pin.Trim();

        var baseUrl = await routing.GetBaseUrlAsync(token, cancellationToken);
        var (deviceKey, certificateBase64) = GenerateDeviceKey();

        var body = new HebeRegisterRequest(
            OS: HebeConstants.Os,
            DeviceModel: HebeConstants.DeviceModel,
            Certificate: certificateBase64,
            CertificateType: HebeConstants.CertificateType,
            CertificateThumbprint: deviceKey.Fingerprint,
            PIN: pin,
            SecurityToken: token,
            SelfIdentifier: Guid.NewGuid().ToString()
            );

        var url = $"{baseUrl}/{symbol}/{RegisterPath}";
        var response = await apiClient.PostAsync<HebeRegisterRequest, HebeRegisterResponse>(
            url, body, deviceKey, cancellationToken);

        return new RegistrationResult(
            deviceKey,
            symbol,
            response.RestUrl,
            response.LoginId,
            response.UserLogin,
            response.UserName
            );
    }

    /// <summary>Self-signed RSA-2048 certificate; Vulcan identifies the device by its SHA-1 fingerprint.</summary>
    internal static (DeviceKey Key, string CertificateBase64) GenerateDeviceKey()
    {
        using var rsa = RSA.Create(2048);

        var certRequest = new CertificateRequest(
            "CN=vulcanbot",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
            );

        using var cert = certRequest.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddYears(20)
            );

        var key = new DeviceKey(
            rsa.ExportPkcs8PrivateKeyPem(),
            cert.ExportCertificatePem(),
            cert.Thumbprint.ToLowerInvariant()
            );

        return (key, Convert.ToBase64String(cert.RawData));
    }
}
