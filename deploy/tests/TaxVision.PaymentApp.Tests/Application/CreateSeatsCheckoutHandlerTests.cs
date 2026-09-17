using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.SeatsCheckouts.Commands;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>
/// El checkout hosteado de una compra de asientos: crea un <see cref="SaaSPayment"/> de tipo
/// <see cref="SaaSPaymentType.SeatsPurchaseCharge"/> con el <c>SeatPurchaseIntentId</c> en
/// <c>TargetAggregateId</c> (la clave de correlación que usa el webhook para aprovisionar), por el monto que
/// Subscription ya resolvió, y devuelve la URL de checkout.
/// </summary>
public sealed class CreateSeatsCheckoutHandlerTests
{
    private static CreateSeatsCheckoutCommand Command(Guid tenantId, Guid intentId, long amountCents) =>
        new(
            tenantId,
            intentId,
            amountCents,
            "USD",
            "buyer@example.com",
            "https://app.example.com/success",
            "https://app.example.com/cancel",
            $"seat-checkout-{intentId:N}"
        );

    [Fact]
    public async Task Creates_a_seats_purchase_charge_correlated_to_the_intent_and_returns_the_url()
    {
        var tenantId = Guid.NewGuid();
        var intentId = Guid.NewGuid();
        var payments = new FakeSaaSPaymentRepository();
        var provider = new FakePaymentProvider();

        var result = await CreateSeatsCheckoutHandler.Handle(
            Command(tenantId, intentId, amountCents: 3000),
            payments,
            new FakePaymentAdapterFactory(provider),
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(),
            new FakePaymentAppMetrics(),
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("https://checkout.example.com/session", result.Value.CheckoutUrl);
        Assert.NotNull(payments.Added);
        Assert.Equal(SaaSPaymentType.SeatsPurchaseCharge, payments.Added!.Type);
        Assert.Equal(intentId, payments.Added.TargetAggregateId);
        Assert.Equal(tenantId, payments.Added.TenantId);
        Assert.Equal(3000, payments.Added.Amount.AmountCents);
        Assert.Equal("USD", payments.Added.Amount.Currency);
        Assert.Equal(3000, provider.LastRequest!.Amount.AmountCents);
    }

    [Fact]
    public async Task Fails_when_the_provider_does_not_support_hosted_checkout()
    {
        var payments = new FakeSaaSPaymentRepository();
        var provider = new FakePaymentProvider(
            PaymentProviderCode.Manual,
            new HashSet<PaymentMethodKind> { PaymentMethodKind.Manual },
            supportsHostedCheckout: false
        );

        var command = Command(Guid.NewGuid(), Guid.NewGuid(), 3000) with
        {
            Provider = PaymentProviderCode.Manual,
            Method = PaymentMethodKind.Manual,
        };

        var result = await CreateSeatsCheckoutHandler.Handle(
            command,
            payments,
            new FakePaymentAdapterFactory(provider),
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(),
            new FakePaymentAppMetrics(),
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentMethod.UnsupportedForCheckout", result.Error.Code);
        Assert.Null(payments.Added);
        Assert.Null(provider.LastRequest);
    }

    private sealed class FakeSaaSPaymentRepository : ISaaSPaymentRepository
    {
        public SaaSPayment? Added { get; private set; }

        public Task<SaaSPayment?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default) =>
            Task.FromResult<SaaSPayment?>(null);

        public Task AddAsync(SaaSPayment payment, CancellationToken ct = default)
        {
            Added = payment;
            return Task.CompletedTask;
        }

        public Task<SaaSPayment?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<SaaSPayment?>(null);

        public Task<SaaSPayment?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<SaaSPayment?>(null);

        public Task<SaaSPayment?> GetByExternalReferenceAsync(
            PaymentProviderCode code,
            string reference,
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
    }

    private sealed class FakePaymentAdapterFactory(IPaymentProvider provider) : IPaymentAdapterFactory
    {
        public IPaymentProvider Resolve(PaymentProviderCode code)
        {
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
