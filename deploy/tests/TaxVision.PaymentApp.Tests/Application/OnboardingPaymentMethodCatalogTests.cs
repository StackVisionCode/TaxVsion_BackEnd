using Microsoft.Extensions.Options;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.PaymentMethods;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Infrastructure.Providers.Catalog;
using TaxVision.PaymentApp.Infrastructure.Providers.PayPal;
using TaxVision.PaymentApp.Infrastructure.Providers.Stripe;

namespace TaxVision.PaymentApp.Tests.Application;

public sealed class OnboardingPaymentMethodCatalogTests
{
    [Fact]
    public async Task Defaults_keep_stripe_card_enabled_and_paypal_hidden_until_adapter_phase()
    {
        var catalog = CreateCatalog(new PaymentMethodCatalogOptions());

        var result = await catalog.GetOptionsAsync(Guid.NewGuid(), "Monthly", "USD");

        Assert.True(result.IsSuccess);
        Assert.Contains(
            result.Value,
            option =>
                option.Provider == PaymentProviderCode.Stripe
                && option.Method == PaymentMethodKind.Card
                && option.Enabled
        );
        Assert.Contains(
            result.Value,
            option =>
                option.Provider == PaymentProviderCode.PayPal
                && option.Method == PaymentMethodKind.Wallet
                && !option.Enabled
        );
    }

    [Fact]
    public async Task Ensure_enabled_rejects_disabled_configured_method()
    {
        var catalog = CreateCatalog(
            new PaymentMethodCatalogOptions
            {
                Onboarding =
                [
                    new ConfiguredOnboardingPaymentMethod
                    {
                        Provider = "Stripe",
                        Method = "Card",
                        DisplayName = "Card",
                        Enabled = false,
                        DisabledReason = "maintenance",
                    },
                ],
            }
        );

        var result = await catalog.EnsureEnabledAsync(
            PaymentProviderCode.Stripe,
            PaymentMethodKind.Card,
            Guid.NewGuid(),
            "Monthly",
            "USD"
        );

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentMethod.Disabled", result.Error.Code);
        Assert.Equal("maintenance", result.Error.Message);
    }

    [Fact]
    public async Task Configured_options_are_scoped_and_sorted()
    {
        var planId = Guid.NewGuid();
        var catalog = CreateCatalog(
            new PaymentMethodCatalogOptions
            {
                Onboarding =
                [
                    new ConfiguredOnboardingPaymentMethod
                    {
                        Provider = "PayPal",
                        Method = "Wallet",
                        DisplayName = "PayPal",
                        Priority = 20,
                        PlanIds = [Guid.NewGuid().ToString("D")],
                    },
                    new ConfiguredOnboardingPaymentMethod
                    {
                        Provider = "Stripe",
                        Method = "Card",
                        DisplayName = "Card",
                        Priority = 10,
                        PlanIds = [planId.ToString("D")],
                        BillingCycles = ["Yearly"],
                        Currencies = ["USD"],
                    },
                ],
            }
        );

        var result = await catalog.GetOptionsAsync(planId, "Yearly", "usd");

        Assert.True(result.IsSuccess);
        var option = Assert.Single(result.Value);
        Assert.Equal(PaymentProviderCode.Stripe, option.Provider);
        Assert.Equal(PaymentMethodKind.Card, option.Method);
    }

    [Fact]
    public async Task Invalid_config_returns_explicit_error()
    {
        var catalog = CreateCatalog(
            new PaymentMethodCatalogOptions
            {
                Onboarding =
                [
                    new ConfiguredOnboardingPaymentMethod
                    {
                        Provider = "NotAProvider",
                        Method = "Card",
                        DisplayName = "Broken",
                    },
                ],
            }
        );

        var result = await catalog.GetOptionsAsync(Guid.NewGuid(), "Monthly", "USD");

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentMethodCatalog.ProviderInvalid", result.Error.Code);
    }

    [Fact]
    public async Task Enabled_provider_is_hidden_when_required_provider_configuration_is_missing()
    {
        var catalog = CreateCatalog(
            new PaymentMethodCatalogOptions
            {
                Onboarding =
                [
                    new ConfiguredOnboardingPaymentMethod
                    {
                        Provider = "PayPal",
                        Method = "Wallet",
                        DisplayName = "PayPal",
                        Enabled = true,
                    },
                ],
            },
            payPal: new PayPalOptions
            {
                ClientId = "paypal-client-id",
                ClientSecret = "",
                WebhookId = "paypal-webhook-id",
            }
        );

        var result = await catalog.GetOptionsAsync(Guid.NewGuid(), "Monthly", "USD");

        Assert.True(result.IsSuccess);
        var option = Assert.Single(result.Value);
        Assert.Equal(PaymentProviderCode.PayPal, option.Provider);
        Assert.False(option.Enabled);
        Assert.Equal("ProviderNotConfigured", option.DisabledReason);
    }

    [Fact]
    public async Task Operational_override_can_disable_configured_provider_without_redeploy()
    {
        var catalog = CreateCatalog(
            new PaymentMethodCatalogOptions(),
            overrides: [Override(PaymentProviderCode.Stripe, PaymentMethodKind.Card, enabled: false, "Maintenance")]
        );

        var result = await catalog.EnsureEnabledAsync(
            PaymentProviderCode.Stripe,
            PaymentMethodKind.Card,
            Guid.NewGuid(),
            "Monthly",
            "USD"
        );

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentMethod.Disabled", result.Error.Code);
        Assert.Equal("Maintenance", result.Error.Message);
    }

    [Fact]
    public async Task Operational_override_can_enable_paypal_when_provider_is_configured()
    {
        var catalog = CreateCatalog(
            new PaymentMethodCatalogOptions(),
            overrides: [Override(PaymentProviderCode.PayPal, PaymentMethodKind.Wallet, enabled: true)]
        );

        var result = await catalog.GetOptionsAsync(Guid.NewGuid(), "Monthly", "USD");

        Assert.True(result.IsSuccess);
        var paypal = Assert.Single(
            result.Value,
            option => option.Provider == PaymentProviderCode.PayPal && option.Method == PaymentMethodKind.Wallet
        );
        Assert.True(paypal.Enabled);
        Assert.Null(paypal.DisabledReason);
    }

    private static ConfiguredOnboardingPaymentMethodCatalog CreateCatalog(
        PaymentMethodCatalogOptions options,
        StripeOptions? stripe = null,
        PayPalOptions? payPal = null,
        IReadOnlyList<OnboardingPaymentMethodOverride>? overrides = null
    ) =>
        new(
            Options.Create(options),
            Options.Create(stripe ?? new StripeOptions { SecretKey = "sk_test_123", WebhookSecret = "whsec_123" }),
            Options.Create(
                payPal
                    ?? new PayPalOptions
                    {
                        ClientId = "paypal-client-id",
                        ClientSecret = "paypal-client-secret",
                        WebhookId = "paypal-webhook-id",
                    }
            ),
            new InMemoryOnboardingPaymentMethodOverrideRepository(overrides ?? [])
        );

    private static OnboardingPaymentMethodOverride Override(
        PaymentProviderCode provider,
        PaymentMethodKind method,
        bool enabled,
        string? disabledReason = null
    ) =>
        OnboardingPaymentMethodOverride
            .Create(provider, method.ToString(), enabled, disabledReason, Guid.NewGuid(), DateTime.UtcNow)
            .Value;

    private sealed class InMemoryOnboardingPaymentMethodOverrideRepository(
        IReadOnlyList<OnboardingPaymentMethodOverride> overrides
    ) : IOnboardingPaymentMethodOverrideRepository
    {
        private readonly List<OnboardingPaymentMethodOverride> _overrides = overrides.ToList();

        public Task<IReadOnlyList<OnboardingPaymentMethodOverride>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OnboardingPaymentMethodOverride>>(_overrides);

        public Task<OnboardingPaymentMethodOverride?> GetAsync(
            PaymentProviderCode providerCode,
            string method,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                _overrides.FirstOrDefault(x =>
                    x.ProviderCode == providerCode
                    && string.Equals(x.Method, method, StringComparison.OrdinalIgnoreCase)
                )
            );

        public Task AddAsync(OnboardingPaymentMethodOverride paymentMethodOverride, CancellationToken ct = default)
        {
            _overrides.Add(paymentMethodOverride);
            return Task.CompletedTask;
        }
    }
}
