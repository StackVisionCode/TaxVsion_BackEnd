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
    private static IReadOnlyDictionary<string, object?> SampleData(
        string quantity = "3",
        string unitPrice = "5.00",
        string logo = "",
        string legalName = "",
        string ein = "",
        string address = ""
    ) =>
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
                    ["logo"] = logo,
                },
                ["billedTo"] = new Dictionary<string, object>
                {
                    ["legalName"] = legalName,
                    ["ein"] = ein,
                    ["address"] = address,
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

    // Pasar a anual cobra plan, add-ons y asientos en un pago: una fila por cada cosa, con su importe.
    [Fact]
    public async Task Renders_one_row_per_charged_item_when_the_charge_has_several()
    {
        var renderer = new TemplateDocumentRenderer(NullLogger<TemplateDocumentRenderer>.Instance);
        var data = (Dictionary<string, object?>)SampleData();
        var receipt = (Dictionary<string, object>)data["receipt"]!;
        receipt["description"] = "Switch to yearly billing";
        receipt["price"] = "1,589.00";
        receipt["hasLines"] = true;
        receipt["lines"] = new List<Dictionary<string, object>>
        {
            Line("Enterprise — yearly", "1", "999.00", "999.00"),
            Line("Email — yearly", "1", "290.00", "290.00"),
            Line("Extra seats (Standard) — yearly", "2", "150.00", "300.00"),
        };

        var result = await renderer.RenderHtmlAsync("saas.receipt.v1", 1, Guid.NewGuid(), data);

        Assert.True(result.IsSuccess);
        var html = result.Value;
        Assert.Contains("Enterprise — yearly", html);
        Assert.Contains("Email — yearly", html);
        Assert.Contains("Extra seats (Standard) — yearly", html);
        Assert.Contains(">2</td>", html);
        Assert.Contains("150.00 USD", html);
        Assert.Contains("300.00 USD", html);
        Assert.Contains("1,589.00 USD", html);
    }

    private static Dictionary<string, object> Line(
        string description,
        string quantity,
        string unitPrice,
        string amount
    ) =>
        new()
        {
            ["description"] = description,
            ["quantity"] = quantity,
            ["unitPrice"] = unitPrice,
            ["amount"] = amount,
        };

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

    // El logo ya es la marca: el nombre del emisor al lado solo se pinta si no hay logo.
    [Fact]
    public async Task Header_shows_only_the_logo_when_there_is_one()
    {
        var renderer = new TemplateDocumentRenderer(NullLogger<TemplateDocumentRenderer>.Instance);

        var result = await renderer.RenderHtmlAsync(
            "saas.receipt.v1",
            1,
            Guid.NewGuid(),
            SampleData(logo: "data:image/png;base64,AAAA")
        );

        Assert.True(result.IsSuccess);
        Assert.Contains("src=\"data:image/png;base64,AAAA\"", result.Value);
        Assert.DoesNotContain("<div class=\"wordmark\">", result.Value);
    }

    [Fact]
    public async Task Header_falls_back_to_the_issuer_name_without_a_logo()
    {
        var renderer = new TemplateDocumentRenderer(NullLogger<TemplateDocumentRenderer>.Instance);

        var result = await renderer.RenderHtmlAsync("saas.receipt.v1", 1, Guid.NewGuid(), SampleData());

        Assert.True(result.IsSuccess);
        Assert.Contains("<div class=\"wordmark\">TaxVision Inc.</div>", result.Value);
    }

    // Con perfil: el nombre legal manda, la oficina queda como DBA y el EIN y la dirección en líneas propias.
    [Fact]
    public async Task Billed_to_shows_the_legal_name_ein_and_address_with_the_office_as_dba()
    {
        var renderer = new TemplateDocumentRenderer(NullLogger<TemplateDocumentRenderer>.Instance);

        var result = await renderer.RenderHtmlAsync(
            "saas.receipt.v1",
            1,
            Guid.NewGuid(),
            SampleData(
                legalName: "Core Tax Professionals LLC",
                ein: "12-3456789",
                address: "1600 Main St, Suite 2, Miami, FL 33101"
            )
        );

        Assert.True(result.IsSuccess);
        var billedTo = BilledToBlock(result.Value);
        Assert.Contains("<div class=\"name\">Core Tax Professionals LLC</div>", billedTo);
        Assert.Contains("<p>DBA CoreTaxPro</p>", billedTo);
        Assert.Contains("<p>EIN 12-3456789</p>", billedTo);
        Assert.Contains("<p>1600 Main St, Suite 2, Miami, FL 33101</p>", billedTo);
        Assert.Contains("<p class=\"sub\">Office</p>", billedTo);
    }

    // Nombre legal igual al de la oficina: no se repite como DBA.
    [Fact]
    public async Task Billed_to_omits_the_dba_when_the_legal_name_is_the_office_name()
    {
        var renderer = new TemplateDocumentRenderer(NullLogger<TemplateDocumentRenderer>.Instance);

        var result = await renderer.RenderHtmlAsync(
            "saas.receipt.v1",
            1,
            Guid.NewGuid(),
            SampleData(legalName: "CoreTaxPro", ein: "12-3456789")
        );

        Assert.True(result.IsSuccess);
        var billedTo = BilledToBlock(result.Value);
        Assert.Contains("<div class=\"name\">CoreTaxPro</div>", billedTo);
        Assert.DoesNotContain("DBA", billedTo);
        Assert.Contains("<p>EIN 12-3456789</p>", billedTo);
    }

    // Sin perfil: el recibo sale como antes, solo con el nombre de la oficina.
    [Fact]
    public async Task Billed_to_without_a_profile_shows_only_the_office_name()
    {
        var renderer = new TemplateDocumentRenderer(NullLogger<TemplateDocumentRenderer>.Instance);

        var result = await renderer.RenderHtmlAsync("saas.receipt.v1", 1, Guid.NewGuid(), SampleData());

        Assert.True(result.IsSuccess);
        var billedTo = BilledToBlock(result.Value);
        Assert.Contains("<div class=\"name\">CoreTaxPro</div>", billedTo);
        Assert.DoesNotContain("DBA", billedTo);
        Assert.DoesNotContain("EIN", billedTo);
        Assert.Single(billedTo.Split("<p").Skip(1));
        Assert.Contains("<p class=\"sub\">Office</p>", billedTo);
    }

    // Solo la columna "Billed to", para no confundirla con el bloque del emisor.
    private static string BilledToBlock(string html)
    {
        var start = html.IndexOf("<h2>Billed to</h2>", StringComparison.Ordinal);
        var end = html.IndexOf("<h2>Issued by</h2>", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return html[start..end];
    }
}
