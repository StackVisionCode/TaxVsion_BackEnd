using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TaxVision.PaymentApp.Infrastructure.Providers.PayPal;

/// <summary>SEC-L2 — verificación RSA offline del header <c>PAYPAL-TRANSMISSION-SIG</c>. Se usa SÓLO
/// cuando el endpoint online de PayPal falla con 5xx/timeout; nunca sustituye al call online como
/// camino principal porque el online también chequea cadena+revocación del cert con sus propios
/// servidores. Algoritmo según PayPal: firma RSA sobre <c>transmission_id|transmission_time|webhook_id|crc32(raw_payload)</c>.</summary>
public sealed class PayPalOfflineWebhookVerifier(
    HttpClient http,
    IOptions<PayPalOfflineFallbackOptions> options,
    ILogger<PayPalOfflineWebhookVerifier> logger
)
{
    private static readonly ExpiringValueCache<string, X509Certificate2> _certCache = new(TimeSpan.FromSeconds(30));

    public async Task<Result<bool>> VerifyAsync(PayPalVerifyWebhookSignatureRequest request, CancellationToken ct)
    {
        var opt = options.Value;
        if (!opt.Enabled)
            return Result.Failure<bool>(new Error("PayPal.OfflineFallback.Disabled", "Offline fallback is disabled."));

        if (!string.Equals(request.AuthAlgo, "SHA256withRSA", StringComparison.OrdinalIgnoreCase))
            return Result.Failure<bool>(
                new Error(
                    "PayPal.OfflineFallback.AlgoUnsupported",
                    $"AuthAlgo '{request.AuthAlgo}' is not supported offline."
                )
            );

        if (!TryBuildAllowedCertUri(request.CertUrl, opt.AllowedCertHosts, out var certUri))
            return Result.Failure<bool>(
                new Error("PayPal.OfflineFallback.CertHostDisallowed", "Cert URL host is not in the allow-list.")
            );

        X509Certificate2 cert;
        try
        {
            cert = await _certCache.GetOrCreateAsync(
                certUri.AbsoluteUri,
                async innerCt =>
                {
                    using var response = await http.GetAsync(certUri, innerCt);
                    response.EnsureSuccessStatusCode();
                    var pem = await response.Content.ReadAsStringAsync(innerCt);
                    var loaded = LoadCertificate(pem);
                    // PayPal rota los certs raramente; aun así respetamos NotAfter.
                    var expiresAt = loaded.NotAfter.ToUniversalTime();
                    var ttlExpiry = DateTime.UtcNow.Add(opt.CertCacheTtl);
                    return (loaded, ttlExpiry < expiresAt ? ttlExpiry : expiresAt);
                },
                ct
            );
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or CryptographicException)
        {
            logger.LogWarning(ex, "PayPal offline fallback failed loading cert from {CertUrl}.", request.CertUrl);
            return Result.Failure<bool>(
                new Error("PayPal.OfflineFallback.CertLoadFailed", "Could not load the PayPal signing certificate.")
            );
        }

        if (DateTime.UtcNow > cert.NotAfter.ToUniversalTime() || DateTime.UtcNow < cert.NotBefore.ToUniversalTime())
            return Result.Failure<bool>(
                new Error("PayPal.OfflineFallback.CertNotYetOrExpired", "Cert is outside its validity window.")
            );

        var crc = Crc32(Encoding.UTF8.GetBytes(request.RawPayload));
        var expectedMessage =
            $"{request.TransmissionId}|{request.TransmissionTime}|{request.WebhookId}|{crc.ToString(CultureInfo.InvariantCulture)}";

        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromBase64String(request.TransmissionSignature);
        }
        catch (FormatException)
        {
            return Result.Failure<bool>(
                new Error("PayPal.OfflineFallback.BadSignatureEncoding", "Transmission signature is not valid Base64.")
            );
        }

        using var rsa = cert.GetRSAPublicKey();
        if (rsa is null)
            return Result.Failure<bool>(
                new Error("PayPal.OfflineFallback.NoRsaKey", "Cert does not contain an RSA public key.")
            );

        var ok = rsa.VerifyData(
            Encoding.UTF8.GetBytes(expectedMessage),
            signatureBytes,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );

        return Result.Success(ok);
    }

    private static bool TryBuildAllowedCertUri(string certUrl, IReadOnlyList<string> allowedHosts, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(certUrl))
            return false;
        if (!Uri.TryCreate(certUrl, UriKind.Absolute, out var parsed))
            return false;
        if (parsed.Scheme != Uri.UriSchemeHttps)
            return false;
        foreach (var host in allowedHosts)
        {
            if (string.Equals(parsed.Host, host, StringComparison.OrdinalIgnoreCase))
            {
                uri = parsed;
                return true;
            }
        }
        return false;
    }

    private static X509Certificate2 LoadCertificate(string pemOrDer)
    {
        // X509CertificateLoader soporta PEM via CreateFromPem; para DER va al constructor binario.
        if (pemOrDer.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal))
            return X509Certificate2.CreateFromPem(pemOrDer);
        return X509CertificateLoader.LoadCertificate(Encoding.ASCII.GetBytes(pemOrDer));
    }

    // CRC32 (IEEE 802.3 / zlib) — PayPal firma el crc32 del payload crudo como entero decimal.
    // Impl inline para no agregar System.IO.Hashing package por un uso puntual.
    private static readonly uint[] _crcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var j = 0; j < 8; j++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
            crc = _crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
