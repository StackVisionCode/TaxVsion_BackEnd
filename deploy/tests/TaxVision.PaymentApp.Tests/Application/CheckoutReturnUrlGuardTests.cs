using Microsoft.Extensions.Options;
using TaxVision.PaymentApp.Application.Common.HostedCheckout;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>Qué URL de regreso acepta el validador (dominio de prueba: <c>taxoffix.io</c>).</summary>
public sealed class CheckoutReturnUrlGuardTests
{
    private static ICheckoutReturnUrlGuard Guard(params string[] extraOrigins) =>
        new CheckoutReturnUrlGuard(
            TestPlatform.Settings,
            Options.Create(new CheckoutReturnUrlOptions { AllowedOrigins = extraOrigins })
        );

    [Theory]
    [InlineData("https://taxoffix.io/account/billing")]
    [InlineData("https://www.taxoffix.io/account/add-ons?checkout=success")]
    [InlineData("https://app.taxoffix.io/storage")]
    [InlineData("https://client.taxoffix.io/payments")]
    [InlineData("https://acme.taxoffix.io/user-management")]
    [InlineData("https://APP.TaxOffix.io/storage")]
    public void Platform_apps_and_office_subdomains_are_allowed(string url)
    {
        Assert.True(Guard().Validate(url, url).IsSuccess);
    }

    [Theory]
    [InlineData("https://evil.com/")]
    [InlineData("https://app.taxoffix.io@evil.com/")]
    [InlineData("https://app.taxoffix.io.evil.com/")]
    [InlineData("https://eviltaxoffix.io/")]
    [InlineData("https://a.b.taxoffix.io/")]
    [InlineData("http://app.taxoffix.io/")]
    [InlineData("https://app.taxoffix.io:8443/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hi")]
    [InlineData("/account/billing")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_rejected(string? url)
    {
        var result = Guard().Validate(url, "https://app.taxoffix.io/");

        Assert.True(result.IsFailure);
        Assert.Equal("Payment.InvalidReturnUrl", result.Error.Code);
    }

    [Fact]
    public void A_bad_cancel_url_is_rejected_too()
    {
        Assert.True(Guard().Validate("https://app.taxoffix.io/", "https://evil.com/").IsFailure);
    }

    [Fact]
    public void Localhost_is_allowed_only_when_its_exact_origin_is_configured()
    {
        Assert.True(Guard().Validate("http://localhost:4200/storage", "http://localhost:4200/storage").IsFailure);

        var dev = Guard("http://localhost:4200");
        Assert.True(dev.Validate("http://localhost:4200/storage", "http://localhost:4200/storage").IsSuccess);
        Assert.True(dev.Validate("http://localhost:4999/storage", "http://localhost:4200/storage").IsFailure);
    }
}
