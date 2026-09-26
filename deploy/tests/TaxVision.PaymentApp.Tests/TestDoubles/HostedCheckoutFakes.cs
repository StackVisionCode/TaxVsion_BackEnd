using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Tests.TestDoubles;

// Dobles compartidos por los tests de los checkouts hosteados: los cuatro tipos corren la misma tubería,
// así que no tiene sentido una copia de estos fakes por handler.

public sealed class FakeSaaSPaymentRepository(SaaSPayment? existing = null) : ISaaSPaymentRepository
{
    public SaaSPayment? Added { get; private set; }

    /// <summary>Lo que devuelve la búsqueda por clave de idempotencia: null fuerza el camino de creación.</summary>
    public SaaSPayment? Existing { get; set; } = existing;

    public Task<SaaSPayment?> GetByIdAsync(Guid saaSPaymentId, Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(ById);

    public Task<SaaSPayment?> GetByIdAsync(Guid saaSPaymentId, CancellationToken ct = default) =>
        Task.FromResult<SaaSPayment?>(null);

    public Task<SaaSPayment?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default) =>
        Task.FromResult(Existing);

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

    public Task<IReadOnlyList<SaaSPayment>> GetSucceededWithoutReceiptAsync(
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

    /// <summary>El pago del onboarding, cuando el test lo necesita.</summary>
    public SaaSPayment? Onboarding { get; set; }

    /// <summary>Lo que devuelve la búsqueda por Id.</summary>
    public SaaSPayment? ById { get; set; }

    public Task<SaaSPayment?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
        Task.FromResult(Onboarding);

    public Task<(IReadOnlyList<SaaSPayment> Items, int TotalCount)> SearchForTenantAsync(
        Guid tenantId,
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

public sealed class FakePaymentAdapterFactory(IPaymentProvider provider) : IPaymentAdapterFactory
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

public sealed class FakePaymentProvider(
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

    public Task<Result<SetupIntentInfo>> CreateSetupIntentAsync(ProviderCustomerToken customer, CancellationToken ct) =>
        throw new NotSupportedException();

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

public sealed class FakePaymentAuditLogWriter : IPaymentAuditLogWriter
{
    public Task AppendAsync(PaymentAuditEntry entry, CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class FakeUnitOfWork : IUnitOfWork
{
    /// <summary>Cuántas veces se persistió: una redelivery no debe volver a escribir.</summary>
    public int SaveCalls { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCalls++;
        return Task.FromResult(1);
    }
}

public sealed class FakePaymentAppMetrics : IPaymentAppMetrics
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

public sealed class FakeCorrelationContext : ICorrelationContext
{
    public string CorrelationId => "test-correlation-id";

    public void Set(string correlationId) { }

    public IDisposable Push(string correlationId) => new NoopScope();

    public sealed class NoopScope : IDisposable
    {
        public void Dispose() { }
    }
}
