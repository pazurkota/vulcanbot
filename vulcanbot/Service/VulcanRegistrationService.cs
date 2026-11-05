using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using vulcanbot.Dto;
using vulcanbot.Dto.Hebe;
using vulcanbot.Service.Hebe;

namespace vulcanbot.Service;

/// <summary>
/// Registers vulcanbot as a new eduVULCAN device using the one-time JWT tokens from https://eduvulcan.pl/api/ap.
/// </summary>
public class VulcanRegistrationService(HebeClient hebe)
{
    public async Task<RegistrationResult> RegisterDeviceAsync(string apInput, CancellationToken cancellationToken)
    {
        var tokensByTenant = EduVulcanApParser.ParseTokensByTenant(apInput);
        var (deviceKey, certificate) = CreateDeviceKey();
        var deviceId = Guid.NewGuid().ToString();
        var accounts = new List<HebeAccount>();

        foreach (var (tenant, tokens) in tokensByTenant)
        {
            var restUrl = $"{HebeClient.BaseUrl}/{tenant}/api";

            await hebe.PostAsync<object>($"{restUrl}/mobile/register/jwt", deviceKey, new
            {
                OS = HebeClient.DeviceOs,
                Certificate = Convert.ToBase64String(certificate),
                CertificateType = "X509",
                DeviceModel = HebeClient.DeviceModel,
                SelfIdentifier = deviceId,
                CertificateThumbprint = deviceKey.Fingerprint,
                Tokens = tokens
            }, pupilId: null, cancellationToken);

            var tenantAccounts = await hebe.GetAsync<List<HebeAccount>>(
                $"{restUrl}/mobile/register/hebe?mode=2", deviceKey, pupilId: null, cancellationToken);
            accounts.AddRange(tenantAccounts ?? []);
        }

        return new RegistrationResult(deviceKey, accounts);
    }

    private static (DeviceKey Key, byte[] Certificate) CreateDeviceKey()
    {
        using var rsa = RSA.Create(2048);

        var certRequest = new CertificateRequest(
            "CN=APP_CERTIFICATE CA Certificate",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
            );

        // Long validity — the certificate is the device's identity and there is no renewal flow.
        using var cert = certRequest.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddYears(20)
            );

        var key = new DeviceKey(
            rsa.ExportPkcs8PrivateKeyPem(),
            cert.ExportCertificatePem(),
            cert.Thumbprint.ToLowerInvariant()
            );

        return (key, cert.RawData);
    }
}
