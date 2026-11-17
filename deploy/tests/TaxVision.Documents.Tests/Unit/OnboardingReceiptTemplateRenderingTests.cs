using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Documents.Infrastructure.Rendering;
using Xunit;

namespace TaxVision.Documents.Tests.Unit;

/// <summary>PayFlow (Fase 10) — renderiza onboarding.receipt.v1 con el motor Fluid real y la forma
/// de datos exacta que produce ProcessOnboardingReceiptGenerationHandler.BuildRenderData (emisor
/// plataforma fijo, sin branding de tenant, sin línea de pago/QR — el pago ya está confirmado).</summary>
public sealed class OnboardingReceiptTemplateRenderingTests
{
    private static IReadOnlyDictionary<string, object?> SampleData(
        string paymentMethodMasked = "Visa •••• 4242",
        string logo = "",
        string? officeName = null
    ) =>
        new Dictionary<string, object?>
        {
            ["receipt"] = WithOfficeName(
                new Dictionary<string, object>
                {
                    ["onboardingId"] = "0123456789abcdef0123456789abcdef",
                    ["payerName"] = "Ada Lovelace",
                    ["payerEmail"] = "ada@example.com",
                    ["planName"] = "Professional",
                    ["planCode"] = "pro-monthly",
                    ["price"] = "49.00",
                    ["currency"] = "USD",
                    ["paidAt"] = "2026-07-28 14:30 UTC",
                    ["transactionReferenceMask"] = "4242",
                    ["paymentMethodMasked"] = paymentMethodMasked,
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
                },
                officeName
            ),
        };

    // Sin officeName la clave no existe: así llegan los payloads de antes de F0.
    private static Dictionary<string, object> WithOfficeName(Dictionary<string, object> receipt, string? officeName)
    {
        if (officeName is not null)
            receipt["officeName"] = officeName;
        return receipt;
    }

    [Fact]
    public async Task Renders_receipt_html_with_all_key_fields()
    {
        var renderer = new TemplateDocumentRenderer(NullLogger<TemplateDocumentRenderer>.Instance);

        var result = await renderer.RenderHtmlAsync("onboarding.receipt.v1", 1, Guid.NewGuid(), SampleData());

        Assert.True(result.IsSuccess);
        var html = result.Value;
        Assert.Contains("Ada Lovelace", html);
        Assert.Contains("ada@example.com", html);
        Assert.Contains("Professional", html);
        Assert.Contains("49.00", html);
        Assert.Contains("USD", html);
        Assert.Contains("TaxVision Inc.", html);
        Assert.Contains("Visa •••• 4242", html);
        Assert.Contains("Payment confirmed", html);
    }

    [Fact]
    public async Task Renders_without_payment_method_line_when_not_provided()
    {
        var renderer = new TemplateDocumentRenderer(NullLogger<TemplateDocumentRenderer>.Instance);

        var result = await renderer.RenderHtmlAsync(
            "onboarding.receipt.v1",
            1,
            Guid.NewGuid(),
            SampleData(paymentMethodMasked: "")
        );

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("Payment method", result.Value);
    }

    [Fact]
    public async Task Unknown_template_fails_closed()
    {
        var renderer = new TemplateDocumentRenderer(NullLogger<TemplateDocumentRenderer>.Instance);

        var result = await renderer.RenderHtmlAsync("does.not.exist", 9, Guid.NewGuid(), SampleData());

        Assert.True(result.IsFailure);
        Assert.Equal("Documents.Template.NotFound", result.Error.Code);
    }

    // El logo ya es la marca: el nombre del emisor al lado solo se pinta si no hay logo.
    [Fact]
    public async Task Header_shows_only_the_logo_when_there_is_one()
    {
        var renderer = new TemplateDocumentRenderer(NullLogger<TemplateDocumentRenderer>.Instance);

        var result = await renderer.RenderHtmlAsync(
            "onboarding.receipt.v1",
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

        var result = await renderer.RenderHtmlAsync("onboarding.receipt.v1", 1, Guid.NewGuid(), SampleData());

        Assert.True(result.IsSuccess);
        Assert.Contains("<div class=\"wordmark\">TaxVision Inc.</div>", result.Value);
    }

    [Fact]
    public async Task Billed_to_shows_the_office_attention_the_payer_when_the_office_name_is_known()
    {
        var renderer = new TemplateDocumentRenderer(NullLogger<TemplateDocumentRenderer>.Instance);

        var result = await renderer.RenderHtmlAsync(
            "onboarding.receipt.v1",
            1,
            Guid.NewGuid(),
            SampleData(officeName: "Gutierrez Tax Office")
        );

        Assert.True(result.IsSuccess);
        Assert.Contains("<div class=\"name\">Gutierrez Tax Office</div>", result.Value);
        Assert.Contains("Attn: Ada Lovelace", result.Value);
        Assert.Contains("ada@example.com", result.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Billed_to_falls_back_to_the_payer_for_old_payloads(string? officeName)
    {
        var renderer = new TemplateDocumentRenderer(NullLogger<TemplateDocumentRenderer>.Instance);

        var result = await renderer.RenderHtmlAsync(
            "onboarding.receipt.v1",
            1,
            Guid.NewGuid(),
            SampleData(officeName: officeName)
        );

        Assert.True(result.IsSuccess);
        Assert.Contains("<div class=\"name\">Ada Lovelace</div>", result.Value);
        Assert.DoesNotContain("Attn:", result.Value);
    }

    [Fact]
    public async Task A_discounted_signup_shows_the_gross_each_code_and_the_paid_total()
    {
        var renderer = new TemplateDocumentRenderer(NullLogger<TemplateDocumentRenderer>.Instance);
        var data = (Dictionary<string, object?>)SampleData();
        var receipt = (Dictionary<string, object>)data["receipt"]!;
        foreach (
            var (key, value) in TaxVision.Documents.Application.Generations.Receipts.ReceiptRenderData.Totals(
                4_900,
                [new("Referral discount", 500), new("Promo code SPRI...0420", 1_000)]
            )
        )
            receipt[key] = value;

        var result = await renderer.RenderHtmlAsync("onboarding.receipt.v1", 1, Guid.NewGuid(), data);

        Assert.True(result.IsSuccess);
        var html = result.Value;
        Assert.Contains("<span>Subtotal</span><span>64.00 USD</span>", html);
        Assert.Contains("Referral discount", html);
        Assert.Contains("&minus;5.00 USD", html);
        Assert.Contains("Promo code SPRI...0420", html);
        Assert.Contains("&minus;10.00 USD", html);
        Assert.Contains("<span class=\"g\">49.00 USD</span>", html);
    }
}
