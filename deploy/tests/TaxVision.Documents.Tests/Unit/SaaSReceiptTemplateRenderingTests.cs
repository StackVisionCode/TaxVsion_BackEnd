using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Documents.Infrastructure.Rendering;
using Xunit;

namespace TaxVision.Documents.Tests.Unit;

/// <summary>
/// Renderiza saas.receipt.v1 con el motor Fluid real y la forma de datos exacta que arma
/// ProcessSaaSReceiptGenerationHandler. Lo que importa acá es que el recibo diga qué se compró y en qué
/// cantidad — "3 × $5.00 = $15.00" — y que siga saliendo bien cuando el cobro no tiene unidades que contar.
/// </summary>
public sealed class SaaSReceiptTemplateRenderingTests
{
    private static IReadOnlyDictionary<string, object?> SampleData(string quantity = "3", string unitPrice = "5.00") =>
        new Dictionary<string, object?>
        {
            ["receipt"] = new Dictionary<string, object>
            {
                ["officeName"] = "CoreTaxPro",
                ["description"] = "Extra seats",
                ["price"] = "15.00",
                ["currency"] = "USD",
                ["paidAt"] = "2026-09-26 00:33 UTC",
                ["transactionReferenceMask"] = "4242",
                ["quantity"] = quantity,
                ["unitPrice"] = unitPrice,
                ["issuer"] = new Dictionary<string, object>
                {
                    ["name"] = "TaxVision Inc.",
                    ["taxId"] = "XX-XXXXXXX",
                    ["addressLine1"] = "1 Market St",
                    ["city"] = "San Francisco",
                    ["state"] = "CA",
                    ["postalCode"] = "94105",
                    ["country"] = "US",
                    ["phone"] = "+1-555-0100",
                    ["email"] = "billing@taxvision.com",
                    ["website"] = "https://taxvision.com",
                    ["logo"] = "",
                },
            },
        };

    [Fact]
    public async Task Renders_what_was_bought_how_many_and_at_what_price()
    {
        var renderer = new TemplateDocumentRenderer(NullLogger<TemplateDocumentRenderer>.Instance);

        var result = await renderer.RenderHtmlAsync("saas.receipt.v1", 1, Guid.NewGuid(), SampleData());

        Assert.True(result.IsSuccess);
        var html = result.Value;
        Assert.Contains("CoreTaxPro", html);
        Assert.Contains("Extra seats", html);
        Assert.Contains("Unit price", html);
        Assert.Contains(">3</td>", html);
        Assert.Contains("5.00 USD", html);
        Assert.Contains("15.00 USD", html);
    }

    // Una prorrata no tiene unidades: la fila cae a 1 × el total, que es lo que de verdad se cobró.
    [Fact]
    public async Task Without_a_breakdown_the_line_falls_back_to_one_times_the_total()
    {
        var renderer = new TemplateDocumentRenderer(NullLogger<TemplateDocumentRenderer>.Instance);

        var result = await renderer.RenderHtmlAsync(
            "saas.receipt.v1",
            1,
            Guid.NewGuid(),
            SampleData(quantity: string.Empty, unitPrice: string.Empty)
        );

        Assert.True(result.IsSuccess);
        var html = result.Value;
        Assert.Contains(">1</td>", html);
        Assert.Contains("15.00 USD", html);
    }
}
