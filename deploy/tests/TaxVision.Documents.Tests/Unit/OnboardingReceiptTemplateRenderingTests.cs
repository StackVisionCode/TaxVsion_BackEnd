using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Documents.Infrastructure.Rendering;
using Xunit;

namespace TaxVision.Documents.Tests.Unit;

/// <summary>PayFlow (Fase 10) — renderiza onboarding.receipt.v1 con el motor Fluid real y la forma
/// de datos exacta que produce ProcessOnboardingReceiptGenerationHandler.BuildRenderData (emisor
/// plataforma fijo, sin branding de tenant, sin línea de pago/QR — el pago ya está confirmado).</summary>
public sealed class OnboardingReceiptTemplateRenderingTests
{
    private static IReadOnlyDictionary<string, object?> SampleData(string paymentMethodMasked = "Visa •••• 4242") =>
        new Dictionary<string, object?>
        {
            ["receipt"] = new Dictionary<string, object>
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
                    ["logo"] = "",
                },
            },
        };

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
        Assert.Contains("Pago confirmado", html);
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
        Assert.DoesNotContain("Método de pago", result.Value);
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
