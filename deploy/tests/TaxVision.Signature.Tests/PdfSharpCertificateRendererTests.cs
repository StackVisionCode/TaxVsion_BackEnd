using System.Security.Cryptography;
using PdfSharp.Fonts;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Infrastructure.Sealing;

namespace TaxVision.Signature.Tests;

public class PdfSharpCertificateRendererTests
{
    public PdfSharpCertificateRendererTests()
    {
        // PdfSharp 6.x exige un IFontResolver explícito (lo registra el arranque del servicio).
        if (GlobalFontSettings.FontResolver is null)
            GlobalFontSettings.FontResolver = new SealingFontResolver();
    }

    private static CertificateSignerEntry Signer(int order, string? ua = null) =>
        new(
            FullName: $"Signer Number {order} With A Fairly Long Display Name",
            Email: $"signer.number.{order}.with.a.very.long.local.part@example-domain.com",
            Order: order,
            Status: SignerStatus.Signed,
            FirstViewedAtUtc: DateTime.UtcNow.AddMinutes(-30),
            ConsentAcceptedAtUtc: DateTime.UtcNow.AddMinutes(-20),
            SignedAtUtc: DateTime.UtcNow.AddMinutes(-order),
            ClientIp: "203.0.113.7",
            UserAgent: ua
        );

    private static CertificateOfCompletionModel Model(
        int signerCount,
        string? issuer = null,
        byte[]? tenantLogo = null
    ) =>
        new(
            SignatureRequestId: Guid.NewGuid(),
            Title: "Resumen operacional con un título deliberadamente largo para forzar el ajuste de línea por ancho medido",
            Category: SignatureCategory.Fiscal,
            CreatedAtUtc: DateTime.UtcNow.AddHours(-2),
            CompletedAtUtc: DateTime.UtcNow,
            DocumentHashPre: new string('a', 64),
            DocumentHashPost: new string('b', 64),
            Signers: Enumerable
                .Range(1, signerCount)
                .Select(i =>
                    Signer(
                        i,
                        ua: "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36 VeryLongToken/abcdefghijklmnopqrstuvwxyz0123456789"
                    )
                )
                .ToList(),
            IssuerName: issuer,
            TenantLogo: tenantLogo
        );

    [Fact]
    public void Renders_a_nonempty_pdf_with_matching_checksum()
    {
        var renderer = new PdfSharpCertificateRenderer();

        var result = renderer.Render(Model(1));

        Assert.NotEmpty(result.CertificatePdfBytes);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(result.CertificatePdfBytes)).ToLowerInvariant(),
            result.ChecksumSha256
        );
        // Cabecera de un PDF válido.
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(result.CertificatePdfBytes, 0, 4));
    }

    [Fact]
    public void Paginates_many_signers_without_throwing()
    {
        var renderer = new PdfSharpCertificateRenderer();

        var result = renderer.Render(Model(12, issuer: "Manfer Tax Office"));

        Assert.NotEmpty(result.CertificatePdfBytes);
    }

    [Fact]
    public void Ignores_an_unreadable_logo_instead_of_failing()
    {
        var renderer = new PdfSharpCertificateRenderer();

        var result = renderer.Render(Model(1, tenantLogo: [1, 2, 3, 4]));

        Assert.NotEmpty(result.CertificatePdfBytes);
    }
}
