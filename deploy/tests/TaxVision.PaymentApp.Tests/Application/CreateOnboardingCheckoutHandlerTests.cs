using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.OnboardingCheckouts.Commands;
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

    private sealed class FakeSubscriptionPlanPricingClient(Result<PlanPrice> result) : ISubscriptionPlanPricingClient
    {
        public Task<Result<PlanPrice>> GetPriceAsync(
            Guid planId,
            string billingCycle,
            CancellationToken ct = default
        ) => Task.FromResult(result);
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
        public IPaymentProvider Resolve(PaymentProviderCode code) => provider;
    }

    private sealed class FakePaymentProvider : IPaymentProvider
    {
        public HostedCheckoutSessionRequest? LastRequest { get; private set; }

        public PaymentProviderCode Code => PaymentProviderCode.Stripe;

        public ProviderCapabilities Capabilities => throw new NotSupportedException();

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
            string rawPayload,
            string signatureHeader,
            string webhookSecret,
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
