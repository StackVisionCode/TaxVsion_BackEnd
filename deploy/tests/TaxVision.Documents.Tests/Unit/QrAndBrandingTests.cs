using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Documents.Infrastructure.Rendering;
using Xunit;

namespace TaxVision.Documents.Tests.Unit;

public sealed class QrAndBrandingTests
{
    [Fact]
    public void Qr_generator_produces_a_png_data_uri()
    {
        var qr = new QrCoderQrGenerator();

        var uri = qr.CreatePngDataUri("https://acme.pay.taxvision.test/i/abc123");

        Assert.StartsWith("data:image/png;base64,", uri);
        var bytes = Convert.FromBase64String(uri["data:image/png;base64,".Length..]);
        // Firma PNG: 89 50 4E 47 0D 0A 1A 0A
        Assert.True(bytes.Length > 8);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, bytes[..8]);
    }

    private static Dictionary<string, object?> Data(
        string status,
        string paymentUrl,
        string paymentQr,
        string logo,
        string brandColor,
        string displayName,
        string footer
    ) =>
        new()
        {
            ["invoice"] = new Dictionary<string, object>
            {
                ["number"] = "F-2026-0042",
                ["taxYear"] = 2026,
                ["currency"] = "EUR",
                ["issueDate"] = "2026-07-25",
                ["dueDate"] = "2026-08-25",
                ["status"] = status,
                ["paidDate"] = "",
                ["paymentUrl"] = paymentUrl,
                ["paymentQr"] = paymentQr,
                ["logo"] = logo,
                ["brandColor"] = brandColor,
                ["displayName"] = displayName,
                ["footer"] = footer,
                ["issuer"] = new Dictionary<string, object>
                {
                    ["name"] = "TaxVision Labs SL",
                    ["taxId"] = "B-1",
                    ["address"] = "",
                },
                ["customer"] = new Dictionary<string, object>
                {
                    ["name"] = "Cliente SA",
                    ["taxId"] = "A-1",
                    ["address"] = "",
                },
                ["lines"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["description"] = "Item",
                        ["quantity"] = "1",
                        ["unitPrice"] = "10.00",
                        ["amount"] = "10.00",
                    },
                },
                ["subtotal"] = "10.00",
                ["taxAmount"] = "2.10",
                ["total"] = "12.10",
                ["notes"] = "",
            },
        };

    [Fact]
    public async Task Branding_and_qr_are_applied_to_the_html()
    {
        var renderer = new TemplateDocumentRenderer(NullLogger<TemplateDocumentRenderer>.Instance);
        var qrDataUri = "data:image/png;base64,AAAA";
        var logoDataUri = "data:image/png;base64,BBBB";

        var result = await renderer.RenderHtmlAsync(
            "billing.invoice.v1",
            1,
            Guid.NewGuid(),
            Data(
                "Pending",
                "https://acme.pay.taxvision.test/i/abc",
                qrDataUri,
                logoDataUri,
                "#8b1e3f",
                "ACME Asesores",
                "ACME · gracias"
            )
        );

        Assert.True(result.IsSuccess);
        var html = result.Value;
        Assert.Contains("--brand: #8b1e3f;", html); // color de marca aplicado
        Assert.Contains("<img class=\"logo\" src=\"" + logoDataUri, html); // logo embebido
        Assert.Contains("ACME Asesores", html); // nombre visible del tenant
        Assert.Contains("ACME · gracias", html); // pie personalizado
        Assert.Contains(qrDataUri, html); // QR del link de pago
        Assert.Contains("Escaneá para pagar", html);
    }
}
