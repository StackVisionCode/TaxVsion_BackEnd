using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.PaymentApp.Infrastructure.Providers.PayPal;

namespace TaxVision.PaymentApp.Tests.Infrastructure.Providers.PayPal;

/// <summary>SEC-L2 — el verifier offline se usa SÓLO como fallback cuando el call online de PayPal cae.
/// Estos tests cubren el camino de la firma RSA + guardas de seguridad (host allow-list, scheme https,
/// flag desactivado).</summary>
public sealed class PayPalOfflineWebhookVerifierTests
{
    private const string AllowedHost = "api.sandbox.paypal.com";
    private const string WebhookId = "WH-TEST-123";
    private const string TransmissionId = "transmission-abc";
    private const string TransmissionTime = "2026-10-15T12:34:56Z";
    private const string RawPayload = "{\"id\":\"evt_1\",\"event_type\":\"PAYMENT.CAPTURE.COMPLETED\"}";

    [Fact]
    public async Task Fails_when_fallback_is_disabled()
    {
        var (verifier, _, _) = BuildVerifier(enabled: false);
        var request = BuildRequest("sig-doesnt-matter", $"https://{AllowedHost}/cert.pem");

        var result = await verifier.VerifyAsync(request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PayPal.OfflineFallback.Disabled", result.Error.Code);
    }

    [Fact]
    public async Task Rejects_cert_url_outside_allow_list()
    {
        var (verifier, _, _) = BuildVerifier(enabled: true);
        var request = BuildRequest("sig", "https://evil.example.com/cert.pem");

        var result = await verifier.VerifyAsync(request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PayPal.OfflineFallback.CertHostDisallowed", result.Error.Code);
    }

    [Fact]
    public async Task Rejects_non_https_cert_url()
    {
        var (verifier, _, _) = BuildVerifier(enabled: true);
        var request = BuildRequest("sig", $"http://{AllowedHost}/cert.pem");

        var result = await verifier.VerifyAsync(request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PayPal.OfflineFallback.CertHostDisallowed", result.Error.Code);
    }

    [Fact]
    public async Task Rejects_unsupported_algorithm()
    {
        var (verifier, _, _) = BuildVerifier(enabled: true);
        var request = BuildRequest("sig", $"https://{AllowedHost}/cert.pem", authAlgo: "HMAC-SHA256");

        var result = await verifier.VerifyAsync(request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PayPal.OfflineFallback.AlgoUnsupported", result.Error.Code);
    }

    [Fact]
    public async Task Returns_true_when_signature_matches_payload()
    {
        using var rsa = RSA.Create(2048);
        using var cert = BuildSelfSignedCert(rsa);
        var pem = ExportCertToPem(cert);
        var certUrl = $"https://{AllowedHost}/cert-{Guid.NewGuid():N}.pem";

        var handler = new FakeCertHandler(pem);
        var (verifier, _, _) = BuildVerifier(enabled: true, handler: handler);

        var crc = Crc32(Encoding.UTF8.GetBytes(RawPayload));
        var messageToSign = $"{TransmissionId}|{TransmissionTime}|{WebhookId}|{crc}";
        var signature = Convert.ToBase64String(
            rsa.SignData(Encoding.UTF8.GetBytes(messageToSign), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
        );

        var request = BuildRequest(signature, certUrl);
        var result = await verifier.VerifyAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : "ok");
        Assert.True(result.Value);
    }

    [Fact]
    public async Task Returns_false_when_signature_does_not_match()
    {
        using var rsa = RSA.Create(2048);
        using var cert = BuildSelfSignedCert(rsa);
        var pem = ExportCertToPem(cert);
        var certUrl = $"https://{AllowedHost}/cert-{Guid.NewGuid():N}.pem";

        var handler = new FakeCertHandler(pem);
        var (verifier, _, _) = BuildVerifier(enabled: true, handler: handler);

        // Firma hecha sobre un payload distinto: la verificación debe retornar false (no exception).
        var tamperedMessage = $"{TransmissionId}|{TransmissionTime}|{WebhookId}|99999";
        var signature = Convert.ToBase64String(
            rsa.SignData(Encoding.UTF8.GetBytes(tamperedMessage), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
        );

        var request = BuildRequest(signature, certUrl);
        var result = await verifier.VerifyAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    private static PayPalVerifyWebhookSignatureRequest BuildRequest(
        string signature,
        string certUrl,
        string authAlgo = "SHA256withRSA"
    ) =>
        new(
            AuthAlgo: authAlgo,
            CertUrl: certUrl,
            TransmissionId: TransmissionId,
            TransmissionSignature: signature,
            TransmissionTime: TransmissionTime,
            WebhookId: WebhookId,
            RawPayload: RawPayload
        );

    private static (PayPalOfflineWebhookVerifier verifier, HttpClient http, FakeCertHandler handler) BuildVerifier(
        bool enabled,
        FakeCertHandler? handler = null
    )
    {
        handler ??= new FakeCertHandler("");
        var http = new HttpClient(handler);
        var options = Options.Create(
            new PayPalOfflineFallbackOptions { Enabled = enabled, AllowedCertHosts = new[] { AllowedHost } }
        );
        var verifier = new PayPalOfflineWebhookVerifier(
            http,
            options,
            NullLogger<PayPalOfflineWebhookVerifier>.Instance
        );
        return (verifier, http, handler);
    }

    private static X509Certificate2 BuildSelfSignedCert(RSA rsa)
    {
        var req = new CertificateRequest("CN=PayPal Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    private static string ExportCertToPem(X509Certificate2 cert)
    {
        var raw = cert.Export(X509ContentType.Cert);
        var b64 = Convert.ToBase64String(raw, Base64FormattingOptions.InsertLineBreaks);
        return "-----BEGIN CERTIFICATE-----\n" + b64 + "\n-----END CERTIFICATE-----";
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var j = 0; j < 8; j++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        foreach (var b in data)
            crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    private sealed class FakeCertHandler(string pem) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(pem, Encoding.UTF8, "application/x-pem-file"),
                }
            );
    }
}
