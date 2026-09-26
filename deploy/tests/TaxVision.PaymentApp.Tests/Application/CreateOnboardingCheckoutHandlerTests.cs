using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.OnboardingCheckouts.Commands;
using TaxVision.PaymentApp.Application.OnboardingPaymentOptions;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>
/// PayFlow (Fase 16) — cierra el price-trust gap: <see cref="CreateOnboardingCheckoutHandler"/>
/// ya no confía en un precio enviado por el caller, sino que lo resuelve vía
/// <see cref="ISubscriptionPlanPricingClient"/>. Estos tests prueban exactamente eso: que el monto
/// cobrado es el que devuelve Subscription, no uno inventado por el test/caller.
/// </summary>
public sealed class CreateOnboardingCheckoutHandlerTests
{
    [Fact]
    public async Task Uses_the_price_resolved_from_subscription_not_a_caller_supplied_value()
    {
        var payments = new FakeSaaSPaymentRepository();
        var provider = new FakePaymentProvider();
        var pricing = new FakeSubscriptionPlanPricingClient(Result.Success(new PlanPrice(4900, "USD")));

        var command = new CreateOnboardingCheckoutCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "buyer@example.com",
            "https://app.example.com/success",
            "https://app.example.com/cancel",
            "onboarding-checkout-key"
        );

        var result = await CreateOnboardingCheckoutHandler.Handle(
            command,
            payments,
            new FakePaymentAdapterFactory(provider),
            new FakeOnboardingPaymentMethodCatalog(),
            pricing,
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(),
            new FakePaymentAppMetrics(),
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.NotNull(provider.LastRequest);
        Assert.Equal(4900, provider.LastRequest!.Amount.AmountCents);
        Assert.Equal("USD", provider.LastRequest.Amount.Currency);
        Assert.NotNull(payments.Added);
        Assert.Equal(4900, payments.Added!.Amount.AmountCents);
    }

    [Fact]
    public async Task Fails_without_creating_a_payment_when_the_plan_price_cannot_be_resolved()
    {
        var payments = new FakeSaaSPaymentRepository();
        var provider = new FakePaymentProvider();
        var pricing = new FakeSubscriptionPlanPricingClient(
            Result.Failure<PlanPrice>(new Error("Subscription.Plan.NotFound", "boom"))
        );

        var command = new CreateOnboardingCheckoutCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "buyer@example.com",
            "https://app.example.com/success",
            "https://app.example.com/cancel",
            "onboarding-checkout-key-2"
        );

        var result = await CreateOnboardingCheckoutHandler.Handle(
            command,
            payments,
            new FakePaymentAdapterFactory(provider),
            new FakeOnboardingPaymentMethodCatalog(),
            pricing,
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(),
            new FakePaymentAppMetrics(),
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.Plan.NotFound", result.Error.Code);
        Assert.Null(payments.Added);
        Assert.Null(provider.LastRequest);
    }

    [Fact]
    public async Task Fails_before_pricing_or_provider_when_the_default_onboarding_method_is_disabled()
    {
        var payments = new FakeSaaSPaymentRepository();
        var provider = new FakePaymentProvider();
        var pricing = new ThrowingSubscriptionPlanPricingClient();

        var command = new CreateOnboardingCheckoutCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "buyer@example.com",
            "https://app.example.com/success",
            "https://app.example.com/cancel",
            "onboarding-checkout-key-disabled"
        );

        var result = await CreateOnboardingCheckoutHandler.Handle(
            command,
            payments,
            new FakePaymentAdapterFactory(provider),
            new FakeOnboardingPaymentMethodCatalog(enabled: false),
            pricing,
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(),
            new FakePaymentAppMetrics(),
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentMethod.Disabled", result.Error.Code);
        Assert.Null(provider.LastRequest);
        Assert.Null(payments.Added);
    }

    [Fact]
    public async Task Uses_the_requested_provider_and_method_for_hosted_checkout()
    {
        var payments = new FakeSaaSPaymentRepository();
        var provider = new FakePaymentProvider(
            PaymentProviderCode.PayPal,
            new HashSet<PaymentMethodKind> { PaymentMethodKind.Wallet }
        );
        var factory = new FakePaymentAdapterFactory(provider);
        var pricing = new FakeSubscriptionPlanPricingClient(Result.Success(new PlanPrice(4900, "USD")));

        var command = new CreateOnboardingCheckoutCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "buyer@example.com",
            "https://app.example.com/success",
            "https://app.example.com/cancel",
            "onboarding-checkout-key-paypal",
            Provider: PaymentProviderCode.PayPal,
            Method: PaymentMethodKind.Wallet
        );

        var result = await CreateOnboardingCheckoutHandler.Handle(
            command,
            payments,
            factory,
            new FakeOnboardingPaymentMethodCatalog(PaymentProviderCode.PayPal, PaymentMethodKind.Wallet),
            pricing,
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(),
            new FakePaymentAppMetrics(),
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentProviderCode.PayPal, factory.LastResolvedCode);
        Assert.Equal(PaymentMethodKind.Wallet, provider.LastRequest!.Method);
        Assert.Equal(PaymentProviderCode.PayPal, payments.Added!.ProviderCode);
    }

    [Fact]
    public async Task Fails_before_creating_session_when_provider_does_not_support_hosted_checkout()
    {
        var payments = new FakeSaaSPaymentRepository();
        var provider = new FakePaymentProvider(
            PaymentProviderCode.Manual,
            new HashSet<PaymentMethodKind> { PaymentMethodKind.Manual },
            supportsHostedCheckout: false
        );

        var command = new CreateOnboardingCheckoutCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "buyer@example.com",
            "https://app.example.com/success",
            "https://app.example.com/cancel",
            "onboarding-checkout-key-manual",
            Provider: PaymentProviderCode.Manual,
            Method: PaymentMethodKind.Manual
        );

        var result = await CreateOnboardingCheckoutHandler.Handle(
            command,
            payments,
            new FakePaymentAdapterFactory(provider),
            new FakeOnboardingPaymentMethodCatalog(PaymentProviderCode.Manual, PaymentMethodKind.Manual),
            new ThrowingSubscriptionPlanPricingClient(),
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(),
            new FakePaymentAppMetrics(),
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentMethod.UnsupportedForCheckout", result.Error.Code);
        Assert.Null(provider.LastRequest);
        Assert.Null(payments.Added);
    }

    [Fact]
    public async Task Retries_a_failed_onboarding_payment_by_reusing_the_aggregate_with_a_new_provider_key()
    {
        const string key = "onboarding-checkout-retrytest";
        var onboardingId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var failed = BuildFailedOnboardingPayment(key, onboardingId, planId);
        var payments = new FakeSaaSPaymentRepository { Existing = failed };
        var provider = new FakePaymentProvider();

        var command = new CreateOnboardingCheckoutCommand(
            onboardingId,
            planId,
            "buyer@example.com",
            "https://app.example.com/success",
            "https://app.example.com/cancel",
            key
        );

        var result = await CreateOnboardingCheckoutHandler.Handle(
            command,
            payments,
            new FakePaymentAdapterFactory(provider),
            new FakeOnboardingPaymentMethodCatalog(),
            // El reintento NO re-resuelve el precio: reusa el aggregate. Si lo llamara, este cliente lanza.
            new ThrowingSubscriptionPlanPricingClient(),
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(),
            new FakePaymentAppMetrics(),
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        // Reusa el aggregate: NO se inserta uno nuevo (respeta el índice único por OnboardingId).
        Assert.Null(payments.Added);
        Assert.Equal(PaymentStatus.Processing, failed.Status);
        // Provider key POR INTENTO: distinto del base → Stripe crea una sesión nueva (no replay del intento
        // fallido) → sin doble cobro.
        Assert.NotNull(provider.LastRequest);
        Assert.Equal($"{key}-1", provider.LastRequest!.IdempotencyKey.Value);
    }

    private static SaaSPayment BuildFailedOnboardingPayment(string key, Guid onboardingId, Guid planId)
    {
        var now = DateTime.UtcNow;
        var payment = SaaSPayment
            .CreateForOnboarding(
                onboardingId,
                IdempotencyKey.Create(key).Value,
                Money.Create(4900, "USD").Value,
                planId,
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                now
            )
            .Value;
        payment.RecordHostedCheckoutSession(
            "sess_old",
            ExternalPaymentReference.Create(PaymentProviderCode.Stripe, "pi_old").Value,
            "https://checkout.example.com/old",
            now
        );
        payment.MarkFailed(
            "card_declined",
            "Your card was declined.",
            willRetry: false,
            nextRetryAtUtc: null,
            Guid.Empty,
            now
        );
        return payment;
    }

    private sealed class FakeSubscriptionPlanPricingClient(Result<PlanPrice> result) : ISubscriptionPlanPricingClient
    {
        public Task<Result<PlanPrice>> GetPriceAsync(
            Guid planId,
            string billingCycle,
            CancellationToken ct = default
        ) => Task.FromResult(result);
    }

    private sealed class ThrowingSubscriptionPlanPricingClient : ISubscriptionPlanPricingClient
    {
        public Task<Result<PlanPrice>> GetPriceAsync(
            Guid planId,
            string billingCycle,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("Pricing must not be called when the payment method is disabled.");
    }

    private sealed class FakeOnboardingPaymentMethodCatalog(
        PaymentProviderCode provider = PaymentProviderCode.Stripe,
        PaymentMethodKind method = PaymentMethodKind.Card,
        bool enabled = true
    ) : IOnboardingPaymentMethodCatalog
    {
        private readonly OnboardingPaymentOption _option = new(
            provider,
            method,
            method.ToString(),
            enabled,
            10,
            enabled ? null : "maintenance"
        );

        public Task<Result<IReadOnlyList<OnboardingPaymentOption>>> GetOptionsAsync(
            Guid planId,
            string billingCycle,
            string? currency = null,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success<IReadOnlyList<OnboardingPaymentOption>>([_option]));

        public Task<Result<IReadOnlyList<OnboardingPaymentOption>>> GetOperationalOptionsAsync(
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success<IReadOnlyList<OnboardingPaymentOption>>([_option]));

        public Task<Result<OnboardingPaymentOption>> EnsureEnabledAsync(
            PaymentProviderCode provider,
            PaymentMethodKind method,
            Guid planId,
            string billingCycle,
            string? currency = null,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                enabled && provider == _option.Provider && method == _option.Method
                    ? Result.Success(_option)
                    : Result.Failure<OnboardingPaymentOption>(new Error("PaymentMethod.Disabled", "maintenance"))
            );
    }
}
