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

    private sealed class FakeSaaSPaymentRepository : ISaaSPaymentRepository
    {
        public SaaSPayment? Added { get; private set; }

        public Task<SaaSPayment?> GetByIdAsync(Guid saaSPaymentId, Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<SaaSPayment?>(null);

        public Task<SaaSPayment?> GetByIdAsync(Guid saaSPaymentId, CancellationToken ct = default) =>
            Task.FromResult<SaaSPayment?>(null);

        public Task<SaaSPayment?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default) =>
            Task.FromResult<SaaSPayment?>(null);

        public Task<SaaSPayment?> GetByExternalReferenceAsync(
            PaymentProviderCode code,
            string providerChargeReference,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<SaaSPayment>> GetStuckProcessingAsync(
            DateTime cutoffUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<SaaSPayment>> GetDueForRetryAsync(
            DateTime nowUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<int> CountDueForRetryAsync(DateTime nowUtc, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<long> SumSucceededAmountCentsAsync(
            SaaSPaymentType type,
            DateTime sinceUtc,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<SaaSPayment>> SearchAdminAsync(
            Guid? tenantId,
            PaymentStatus? status,
            SaaSPaymentType? type,
            DateTime? from,
            DateTime? to,
            int page,
            int pageSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task AddAsync(SaaSPayment payment, CancellationToken ct = default)
        {
            Added = payment;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePaymentAdapterFactory(IPaymentProvider provider) : IPaymentAdapterFactory
    {
        public PaymentProviderCode? LastResolvedCode { get; private set; }

        public IPaymentProvider Resolve(PaymentProviderCode code)
        {
            LastResolvedCode = code;
            if (provider.Code != code)
                throw new InvalidOperationException($"No provider for {code}.");

            return provider;
        }
    }

    private sealed class FakePaymentProvider(
        PaymentProviderCode code = PaymentProviderCode.Stripe,
        IReadOnlySet<PaymentMethodKind>? supportedMethods = null,
        bool supportsHostedCheckout = true
    ) : IPaymentProvider
    {
        public HostedCheckoutSessionRequest? LastRequest { get; private set; }

        public PaymentProviderCode Code => code;

        public ProviderCapabilities Capabilities { get; } =
            new()
            {
                Code = code,
                DisplayName = code.ToString(),
                SupportsOneShotCharge = true,
                SupportsRecurringCharge = false,
                SupportsHostedCheckoutRedirect = supportsHostedCheckout,
                SupportsInlineElements = false,
                SupportsWebhookSignatureVerification = true,
                SupportedMethods = supportedMethods ?? new HashSet<PaymentMethodKind> { PaymentMethodKind.Card },
                SupportsPartialRefund = true,
                Supports3DSecure = true,
                SupportsSavedPaymentMethods = false,
                SupportsMultiCurrency = true,
                SupportsMarketplaceConnect = false,
                SupportsIdempotencyKeys = true,
                SupportsCardTokenization = false,
                RequiresCustomerRegistrationBeforeCharge = false,
                SupportedCurrencies = new HashSet<string> { "USD" },
                SupportedCountries = new HashSet<string> { "US" },
                TypicalAuthorizeLatency = TimeSpan.Zero,
                SuggestedRetryCount = 0,
            };

        public Task<Result<ProviderCustomerToken>> GetOrCreateCustomerAsync(
            Guid tenantId,
            string email,
            string? name,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Result<ChargeAuthorizationResult>> AuthorizeChargeAsync(
            ChargeAuthorizationRequest request,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Result<CaptureResult>> CaptureAsync(
            string providerChargeReference,
            Money amount,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Result<RefundResult>> RefundAsync(
            string providerChargeReference,
            Money amount,
            string reason,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Result<WebhookVerificationResult>> VerifyWebhookSignatureAsync(
            ProviderWebhookVerificationRequest request,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Result<WebhookEventPayload>> ParseWebhookEventAsync(
            string rawPayload,
            string eventType,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Result<ChargeAuthorizationResult>> GetChargeStatusAsync(
            string providerChargeReference,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Result<ChargeAuthorizationResult>> FinalizeHostedCheckoutAsync(
            string providerChargeReference,
            Money amount,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Result<SetupIntentInfo>> CreateSetupIntentAsync(
            ProviderCustomerToken customer,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Result<SavedPaymentMethodInfo>> AttachPaymentMethodAsync(
            ProviderCustomerToken customer,
            string paymentMethodReference,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Result> DetachPaymentMethodAsync(string paymentMethodReference, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<Result<HostedCheckoutSessionResult>> CreateHostedCheckoutSessionAsync(
            HostedCheckoutSessionRequest request,
            CancellationToken ct
        )
        {
            LastRequest = request;
            return Task.FromResult(
                Result.Success(
                    new HostedCheckoutSessionResult("sess_123", "pi_123", "https://checkout.example.com/session")
                )
            );
        }
    }

    private sealed class FakePaymentAuditLogWriter : IPaymentAuditLogWriter
    {
        public Task AppendAsync(PaymentAuditEntry entry, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }

    private sealed class FakePaymentAppMetrics : IPaymentAppMetrics
    {
        public void RecordAttempted(string provider, string type) { }

        public void RecordSucceeded(string provider, string type) { }

        public void RecordFailed(string provider, string type, string failureCode) { }

        public void RecordRefunded(string provider) { }

        public void RecordChargedBack(string provider) { }

        public void RecordWebhookReceived(string provider) { }

        public void RecordWebhookDuplicate(string provider) { }

        public void RecordWebhookSignatureFailed(string provider) { }

        public void RecordProviderLatency(double milliseconds, string provider, string method) { }
    }

    private sealed class FakeCorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "test-correlation-id";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new NoopScope();

        private sealed class NoopScope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
