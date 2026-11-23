using BuildingBlocks.Results;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.Common.HostedCheckout;
using TaxVision.PaymentApp.Application.OnboardingPaymentOptions;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Tests.Architecture;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Application;

public sealed class CheckoutPaymentMethodGuardTests
{
    private static readonly OnboardingPaymentOption Card = new(
        PaymentProviderCode.Stripe,
        PaymentMethodKind.Card,
        "Card",
        Enabled: true,
        Priority: 10,
        DisabledReason: null
    );

    private static readonly OnboardingPaymentOption PayPalOff = new(
        PaymentProviderCode.PayPal,
        PaymentMethodKind.Wallet,
        "PayPal",
        Enabled: false,
        Priority: 20,
        DisabledReason: "maintenance"
    );

    [Fact]
    public async Task Enabled_method_passes()
    {
        var result = await CheckoutPaymentMethodGuard.EnsureAvailableAsync(
            new FakePaymentMethodCatalog(Card, PayPalOff),
            PaymentProviderCode.Stripe,
            PaymentMethodKind.Card,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Method_turned_off_by_the_platform_is_rejected()
    {
        var result = await CheckoutPaymentMethodGuard.EnsureAvailableAsync(
            new FakePaymentMethodCatalog(Card, PayPalOff),
            PaymentProviderCode.PayPal,
            PaymentMethodKind.Wallet,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentMethod.Disabled", result.Error.Code);
        Assert.Equal("maintenance", result.Error.Message);
    }

    [Fact]
    public async Task Method_missing_from_the_catalog_is_rejected()
    {
        var result = await CheckoutPaymentMethodGuard.EnsureAvailableAsync(
            new FakePaymentMethodCatalog(Card),
            PaymentProviderCode.PayPal,
            PaymentMethodKind.Wallet,
            CancellationToken.None
        );

        Assert.Equal("PaymentMethod.Disabled", result.Error.Code);
    }

    [Fact]
    public async Task Catalog_error_is_returned()
    {
        var result = await CheckoutPaymentMethodGuard.EnsureAvailableAsync(
            new FakePaymentMethodCatalog(new Error("PaymentMethodCatalog.ProviderInvalid", "bad config")),
            PaymentProviderCode.Stripe,
            PaymentMethodKind.Card,
            CancellationToken.None
        );

        Assert.Equal("PaymentMethodCatalog.ProviderInvalid", result.Error.Code);
    }

    // Los checkouts fuera de onboarding validan el método antes de hablar con el proveedor.
    [Theory]
    [InlineData("SubscriptionRenewalCheckouts/Commands/CreateSubscriptionRenewalCheckoutHandler.cs")]
    [InlineData("SeatsCheckouts/Commands/CreateSeatsCheckoutHandler.cs")]
    [InlineData("SeatsCheckouts/Commands/CreateSeatReactivationCheckoutHandler.cs")]
    [InlineData("PlanChangeCheckouts/Commands/CreatePlanChangeCheckoutHandler.cs")]
    [InlineData("AddOnCheckouts/Commands/CreateAddOnCheckoutHandler.cs")]
    [InlineData("WalletTopUpCheckouts/Commands/CreateWalletTopUpCheckoutHandler.cs")]
    public void Checkout_handler_checks_the_method_before_the_provider(string handler)
    {
        var source = File.ReadAllText(
            HandlerSourceLocator.FindRepoFile($"src/Services/PaymentApp/TaxVision.PaymentApp.Application/{handler}")
        );

        var guard = source.IndexOf("CheckoutPaymentMethodGuard.EnsureAvailableAsync", StringComparison.Ordinal);
        var pipeline = source.IndexOf("HostedCheckoutPipeline.RunAsync", StringComparison.Ordinal);

        Assert.True(guard >= 0, $"{handler} does not check the payment method.");
        Assert.True(guard < pipeline, $"{handler} checks the payment method after creating the session.");
    }

    [Fact]
    public void Every_hosted_checkout_is_covered()
    {
        var root = HandlerSourceLocator.FindRepoDirectory("src/Services/PaymentApp/TaxVision.PaymentApp.Application");
        var callers = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("HostedCheckoutPipeline.cs", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("HostedCheckoutPipeline.RunAsync", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f))
            .OfType<string>()
            .Order()
            .ToArray();

        // Onboarding ya valida con su propio alcance (plan, ciclo y moneda).
        Assert.Equal(
            [
                "CreateAddOnCheckoutHandler.cs",
                "CreateOnboardingCheckoutHandler.cs",
                "CreatePlanChangeCheckoutHandler.cs",
                "CreateSeatReactivationCheckoutHandler.cs",
                "CreateSeatsCheckoutHandler.cs",
                "CreateSubscriptionRenewalCheckoutHandler.cs",
                "CreateWalletTopUpCheckoutHandler.cs",
            ],
            callers
        );
    }
}
