using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using vulcanbot.Dto;

namespace vulcanbot.Service;

public class VulcanRegistrationService(HttpClient client)
{
    public async Task<RegistrationResult> RegisterDeviceAsync(
        string token,
        string pin,
        string symbol,
        CancellationToken cancellationToken
        )
    {
        using var rsa = RSA.Create(2048);

        var certRequest = new CertificateRequest(
            "CN=OpenVulcan",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
            );

        using var cert = certRequest.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddYears(1)
            );

        var privatePemKey = rsa.ExportPkcs8PrivateKeyPem();
        var certPem = cert.ExportCertificatePem();
        var fingerprint = cert.Thumbprint.ToLowerInvariant();

        var payload = new
        {
            Pin = pin,
            Token = token,
            AppVersion = "24.0.0",
            DeviceId = Guid.NewGuid().ToString(),
            DeviceName = "vulcanbot",
            DeviceModel = ".NET 10",
            DeviceType = "Android",
            Certificate = Convert.ToBase64String(cert.RawData),
            CertificateType = "X509"
        };
        
        // HTTP
        var requestUri = $"https://certyfikaty.vulcan.net.pl/{symbol}/api/mobile/register/new";
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        
        request.Headers.Add("User-Agent", "Dart 3.0 (dart:io)");
        request.Headers.Add("vOS", "Android");
        request.Headers.Add("vDeviceModel", ".NET 10");
        request.Headers.Add("vAPI", "1");
        request.Headers.Add("vDate", DateTimeOffset.UtcNow.ToString("r"));

        var response = await client.SendAsync(request, cancellationToken);
        var jsonResponse = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Registration failed. " +
                                                                               $"Status: {response.StatusCode}" +
                                                                               $"Message: {jsonResponse}");

        var deviceKey = new DeviceKey(privatePemKey, certPem, fingerprint);
        return new RegistrationResult(deviceKey, symbol, requestUri);
    }
}