using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Documents.Infrastructure.Rendering;
using Xunit;

namespace TaxVision.Documents.Tests.Unit;

/// <summary>
/// Renderiza billing.invoice.v1 con el motor Fluid real y la forma de datos exacta que produce
/// <c>ProcessInvoiceGenerationHandler.BuildRenderData</c> (diccionarios anidados + lista de líneas).
/// Blinda la plantilla embebida y el binding contra typos — es el HTML que luego va a Chromium.
/// </summary>
public sealed class InvoiceTemplateRenderingTests
{
    private static IReadOnlyDictionary<string, object?> SampleData(
        string status = "Pending",
        string paidDate = "",
        string paymentUrl = ""
    ) =>
        new Dictionary<string, object?>
        {
            ["invoice"] = new Dictionary<string, object>
            {
                ["number"] = "F-2026-0042",
                ["taxYear"] = 2026,
                ["currency"] = "EUR",
                ["issueDate"] = "2026-07-25",
                ["dueDate"] = "2026-08-25",
                ["status"] = status,
                ["paidDate"] = paidDate,
                ["paymentUrl"] = paymentUrl,
                ["paymentQr"] = "",
                ["logo"] = "",
                ["brandColor"] = "#2563eb",
                ["displayName"] = "TaxVision Labs SL",
                ["footer"] = "Documento generado por TaxVision",
                ["issuer"] = new Dictionary<string, object>
                {
                    ["name"] = "TaxVision Labs SL",
                    ["taxId"] = "B-12345678",
                    ["address"] = "Calle Mayor 1, Madrid",
                },
                ["customer"] = new Dictionary<string, object>
                {
                    ["name"] = "Cliente Ejemplo SA",
                    ["taxId"] = "A-87654321",
                    ["address"] = "",
                },
                ["lines"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["description"] = "Consultoría fiscal Q2",
                        ["quantity"] = "10",
                        ["unitPrice"] = "100.00",
                        ["amount"] = "1,000.00",
                    },
                },
                ["subtotal"] = "1,000.00",
                ["taxAmount"] = "210.00",
                ["total"] = "1,210.00",
                ["notes"] = "Gracias por su confianza.",
            },
        };

    [Fact]
    public async Task Renders_invoice_html_with_all_key_fields()
    {
        var renderer = new TemplateDocumentRenderer(NullLogger<TemplateDocumentRenderer>.Instance);

        var result = await renderer.RenderHtmlAsync("billing.invoice.v1", 1, Guid.NewGuid(), SampleData());

        Assert.True(result.IsSuccess);
        var html = result.Value;
        Assert.Contains("F-2026-0042", html);
        Assert.Contains("TaxVision Labs SL", html);
        Assert.Contains("Cliente Ejemplo SA", html);
        Assert.Contains("Consultoría fiscal Q2", html);
        Assert.Contains("1,210.00", html);
        Assert.Contains("EUR", html);
        Assert.Contains("Gracias por su confianza.", html);
        // El address vacío del cliente no debe dejar una línea de dirección.
        Assert.DoesNotContain("<p></p>", html);
    }

    [Fact]
    public async Task Paid_invoice_shows_watermark_and_no_payment_button()
    {
        var renderer = new TemplateDocumentRenderer(NullLogger<TemplateDocumentRenderer>.Instance);

        var result = await renderer.RenderHtmlAsync(
            "billing.invoice.v1",
            1,
            Guid.NewGuid(),
            SampleData(status: "Paid", paidDate: "2026-07-30", paymentUrl: "https://pay.taxvision.test/x")
        );

        Assert.True(result.IsSuccess);
        var html = result.Value;
        Assert.Contains("<div class=\"watermark paid\"", html); // overlay PAGADO renderizado
        Assert.Contains("Factura pagada", html);
        Assert.Contains("2026-07-30", html);
        // Pagada ⇒ no se ofrece botón de pago aunque venga una URL (el <a>, no el CSS .pay-btn).
        Assert.DoesNotContain("<a class=\"pay-btn\"", html);
    }

    [Fact]
    public async Task Pending_invoice_shows_payment_button_and_link()
    {
        var renderer = new TemplateDocumentRenderer(NullLogger<TemplateDocumentRenderer>.Instance);
        var url = "https://pay.taxvision.test/inv/F-2026-0042";

        var result = await renderer.RenderHtmlAsync(
            "billing.invoice.v1",
            1,
            Guid.NewGuid(),
            SampleData(status: "Pending", paymentUrl: url)
        );

        Assert.True(result.IsSuccess);
        var html = result.Value;
        Assert.Contains("<a class=\"pay-btn\"", html);
        Assert.Contains(url, html);
        Assert.DoesNotContain("<div class=\"watermark", html); // pendiente ⇒ sin overlay de marca de agua
    }

    [Fact]
    public async Task Unknown_template_fails_closed()
    {
        var renderer = new TemplateDocumentRenderer(NullLogger<TemplateDocumentRenderer>.Instance);

        var result = await renderer.RenderHtmlAsync("does.not.exist", 9, Guid.NewGuid(), SampleData());

        Assert.True(result.IsFailure);
        Assert.Equal("Documents.Template.NotFound", result.Error.Code);
    }
}
