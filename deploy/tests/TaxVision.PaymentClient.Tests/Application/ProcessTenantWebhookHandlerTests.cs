using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ProcessTenantWebhook;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.PaymentLinks;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Domain.Webhooks;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>
/// Verifica el hardening de seguridad de <see cref="ProcessTenantWebhookHandler"/>:
/// F1 idempotencia status-aware (un evento terminal se descarta; uno Failed se re-procesa reusando
/// la fila) y F2 verificación de monto (un éxito cuyo importe reportado no coincide con el cargo NO
/// se aplica y queda Stale). Todos los fakes son <c>file</c>-scoped y no se comparten con otros
/// archivos de test.
/// </summary>
public sealed class ProcessTenantWebhookHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Reprocesses_a_previously_failed_event_on_provider_retry_so_the_payment_is_not_lost()
    {
        // F1: la entrega anterior dejó la fila en Failed (no terminal) sin aplicar el cargo. El
        // reintento del provider debe RE-PROCESARLA reusando esa fila, no descartarla como duplicado.
        var payment = CreateProcessingPayment();
        var existing = WebhookEvent
            .Receive(TenantId, PaymentProviderCode.Stripe, "evt_x", "payment_intent.succeeded", "{}", "sig", Now())
            .Value;
        existing.MarkProcessing(Now());
        existing.MarkFailed("transient", Now());

        var provider = new FakePaymentProvider(
            new WebhookVerificationResult("evt_x", "payment_intent.succeeded", "{}"),
            new WebhookEventPayload("pi_123", PaymentStatus.Succeeded, null, null, null)
        );
        var webhooks = new FakeWebhookEventRepository(existing);

        var result = await InvokeAsync(payment, provider, webhooks);

        Assert.True(result.IsSuccess);
        Assert.True(provider.ParseWasCalled); // se re-procesó, NO se descartó
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(WebhookEventStatus.Applied, existing.Status); // reusó la fila existente
        Assert.Null(webhooks.Added); // no insertó otra fila
    }

    [Fact]
    public async Task Discards_an_already_applied_duplicate_without_reparsing()
    {
        // F1: un evento en estado terminal (Applied) ya fue resuelto → se descarta como duplicado.
        var payment = CreateProcessingPayment();
        var existing = WebhookEvent
            .Receive(TenantId, PaymentProviderCode.Stripe, "evt_x", "payment_intent.succeeded", "{}", "sig", Now())
            .Value;
        existing.MarkProcessing(Now());
        existing.MarkApplied(payment.Id, Now());

        var provider = new FakePaymentProvider(
            new WebhookVerificationResult("evt_x", "payment_intent.succeeded", "{}"),
            new WebhookEventPayload("pi_123", PaymentStatus.Succeeded, null, null, null)
        );
        var webhooks = new FakeWebhookEventRepository(existing);

        var result = await InvokeAsync(payment, provider, webhooks);

        Assert.True(result.IsSuccess);
        Assert.False(provider.ParseWasCalled); // no volvió a parsear
        Assert.Null(webhooks.Added); // no insertó otra fila
    }

    [Fact]
    public async Task Amount_mismatch_on_a_success_event_is_not_applied_and_the_event_is_marked_stale()
    {
        // F2: el pago espera 4900 USD; el provider reporta solo 100. Aunque el evento esté firmado,
        // NO debe marcarse Succeeded por un importe distinto — queda Stale y el pago sin tocar.
        var payment = CreateProcessingPayment();
        var provider = new FakePaymentProvider(
            new WebhookVerificationResult("evt_new", "payment_intent.succeeded", "{}"),
            new WebhookEventPayload(
                "pi_123",
                PaymentStatus.Succeeded,
                null,
                null,
                null,
                PaidAmountCents: 100,
                PaidCurrency: "USD"
            )
        );
        var webhooks = new FakeWebhookEventRepository();

        var result = await InvokeAsync(payment, provider, webhooks);

        Assert.True(result.IsSuccess); // no reintenta en bucle: se registra y no se aplica
        Assert.Equal(PaymentStatus.Processing, payment.Status); // NO quedó Succeeded
        Assert.Equal(WebhookEventStatus.Stale, webhooks.Added!.Status);
    }

    [Fact]
    public async Task Matching_amount_on_a_success_event_is_applied()
    {
        // F2 (contraparte): monto coincide y la moneda case-insensitive ("usd") → se aplica normal.
        var payment = CreateProcessingPayment();
        var provider = new FakePaymentProvider(
            new WebhookVerificationResult("evt_new", "payment_intent.succeeded", "{}"),
            new WebhookEventPayload(
                "pi_123",
                PaymentStatus.Succeeded,
                null,
                null,
                null,
                PaidAmountCents: 4900,
                PaidCurrency: "usd"
            )
        );
        var webhooks = new FakeWebhookEventRepository();

        var result = await InvokeAsync(payment, provider, webhooks);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(WebhookEventStatus.Applied, webhooks.Added!.Status);
    }

    // Arma el handler con todos los fakes; el config del tenant lleva un WebhookSecretEncrypted válido.
    private static Task<Result> InvokeAsync(
        TenantPayment payment,
        IPaymentProvider provider,
        IWebhookEventRepository webhooks
    ) =>
        ProcessTenantWebhookHandler.Handle(
            new ProcessTenantWebhookCommand(TenantId, PaymentProviderCode.Stripe, "{}", "sig"),
            new FakeTenantPaymentConfigRepository(BuildActiveConfig()),
            new FakePaymentAdapterFactory(provider),
            new FakeSecretProtector(),
            webhooks,
            new FakeTenantPaymentRepository(payment),
            new FakePaymentLinkRepository(),
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            new FakePaymentClientMetrics(),
            new FakeCorrelationContext(),
            NoOpLogger<WebhookEvent>.Instance,
            CancellationToken.None
        );

    // TenantPayment en Processing con Amount = 4900 USD y ExternalChargeReference.Value == "pi_123".
    private static TenantPayment CreateProcessingPayment()
    {
        var payment = TenantPayment
            .Create(
                TenantId,
                IdempotencyKey.Create("charge-key").Value,
                Money.Create(4900, "USD").Value,
                Guid.NewGuid(),
                PaymentPurpose.Create(PaymentPurposeKind.InvoicePayment, "inv-1").Value,
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("ACME TAX SVC").Value,
                Guid.Empty,
                Now()
            )
            .Value;

        payment.MarkProcessing(
            ExternalPaymentReference.Create(PaymentProviderCode.Stripe, "pi_123").Value,
            providerResponseCode: null,
            providerResponseBody: null,
            Guid.Empty,
            Now()
        );

        return payment;
    }

    private static TenantPaymentConfig BuildActiveConfig()
    {
        var config = TenantPaymentConfig
            .Create(
                TenantId,
                PaymentProviderCode.Stripe,
                TenantPaymentMode.DirectApiKeys,
                "pk_test_123",
                StatementDescriptor.Create("ACME TAX SVC").Value,
                Now()
            )
            .Value;
        config.UpdateSecrets(
            EncryptedSecret.Create("cipher-key").Value,
            EncryptedSecret.Create("cipher-webhook").Value,
            Guid.Empty,
            Now()
        );
        config.MarkActive(Guid.Empty, Now());
        return config;
    }

    private static DateTime Now() => DateTime.UtcNow;
}

file sealed class FakeWebhookEventRepository(WebhookEvent? existing = null) : IWebhookEventRepository
{
    public WebhookEvent? Added { get; private set; }

    public Task<WebhookEvent?> GetByProviderEventIdAsync(
        Guid tenantId,
        PaymentProviderCode code,
        string providerEventId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            existing is not null
            && existing.TenantId == tenantId
            && existing.ProviderCode == code
            && existing.ProviderEventId == providerEventId
                ? existing
                : null
        );

    public Task AddAsync(WebhookEvent webhookEvent, CancellationToken ct = default)
    {
        Added = webhookEvent;
        return Task.CompletedTask;
    }
}

file sealed class FakePaymentProvider(WebhookVerificationResult verification, WebhookEventPayload payload)
    : IPaymentProvider
{
    public bool ParseWasCalled { get; private set; }

    public PaymentProviderCode Code => PaymentProviderCode.Stripe;

    public Task<Result<WebhookVerificationResult>> VerifyWebhookSignatureAsync(
        string rawPayload,
        string signatureHeader,
        string webhookSecret,
        CancellationToken ct
    ) => Task.FromResult(Result.Success(verification));

    public Task<Result<WebhookEventPayload>> ParseWebhookEventAsync(
        string rawPayload,
        string eventType,
        CancellationToken ct
    )
    {
        ParseWasCalled = true;
        return Task.FromResult(Result.Success(payload));
    }

    public Task<Result<ChargeAuthorizationResult>> AuthorizeChargeAsync(
        TenantProviderCredentials credentials,
        ChargeAuthorizationRequest request,
        CancellationToken ct
    ) => throw new NotImplementedException();

    public Task<Result<RefundResult>> RefundAsync(
        TenantProviderCredentials credentials,
        string providerChargeReference,
        Money amount,
        string reason,
        CancellationToken ct
    ) => throw new NotImplementedException();
}

file sealed class FakePaymentAdapterFactory(IPaymentProvider provider) : IPaymentAdapterFactory
{
    public IPaymentProvider Resolve(PaymentProviderCode code) => provider;

    public bool IsRegistered(PaymentProviderCode code) => true;
}

file sealed class FakePaymentLinkRepository : IPaymentLinkRepository
{
    public Task<PaymentLink?> GetByIdAsync(Guid paymentLinkId, Guid tenantId, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<PaymentLink?> GetByTokenAsync(string token, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<PaymentLink?> GetByRelatedTenantPaymentIdAsync(Guid tenantPaymentId, CancellationToken ct = default) =>
        Task.FromResult<PaymentLink?>(null);

    public Task<PaymentLink?> GetActiveByExternalReferenceAsync(
        Guid tenantId,
        string externalReferenceId,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<IReadOnlyList<PaymentLink>> SearchByTenantAsync(
        Guid tenantId,
        PaymentLinkStatus? status,
        int page,
        int pageSize,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<IReadOnlyList<PaymentLink>> GetActiveExpiredBeforeAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task AddAsync(PaymentLink link, CancellationToken ct = default) => throw new NotImplementedException();
}

file sealed class FakeSecretProtector : ISecretProtector
{
    public string Protect(string plaintext) => plaintext;

    public bool TryUnprotect(string? protectedValue, out string plaintext, out SecretUnprotectFailure failure)
    {
        plaintext = "whsec_test";
        failure = default;
        return true;
    }
}

file sealed class FakeTenantPaymentRepository(TenantPayment payment) : ITenantPaymentRepository
{
    public Task<TenantPayment?> GetByIdAsync(Guid tenantPaymentId, Guid tenantId, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<TenantPayment?> GetByIdempotencyKeyAsync(
        Guid tenantId,
        string idempotencyKey,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<TenantPayment?> GetByExternalReferenceAsync(
        Guid tenantId,
        PaymentProviderCode code,
        string providerChargeReference,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            payment.TenantId == tenantId
            && payment.ProviderCode == code
            && payment.ExternalChargeReference?.Value == providerChargeReference
                ? payment
                : null
        );

    public Task<IReadOnlyList<TenantPayment>> GetStuckProcessingAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<IReadOnlyList<TenantPayment>> GetDueForRetryAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<IReadOnlyList<TenantPayment>> SearchAdminAsync(
        Guid? tenantId,
        PaymentStatus? status,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task AddAsync(TenantPayment payment, CancellationToken ct = default) => throw new NotImplementedException();
}

file sealed class FakeTenantPaymentConfigRepository(TenantPaymentConfig config) : ITenantPaymentConfigRepository
{
    public Task<TenantPaymentConfig?> GetByTenantAndProviderAsync(
        Guid tenantId,
        PaymentProviderCode code,
        CancellationToken ct = default
    ) => Task.FromResult<TenantPaymentConfig?>(config);

    public Task<TenantPaymentConfig?> GetByIdAsync(
        Guid tenantPaymentConfigId,
        Guid tenantId,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<IReadOnlyList<TenantPaymentConfig>> GetActiveByTenantAsync(
        Guid tenantId,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<IReadOnlyList<TenantPaymentConfig>> GetAllByTenantAsync(
        Guid tenantId,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task AddAsync(TenantPaymentConfig config, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public void Remove(TenantPaymentConfig config) => throw new NotImplementedException();
}

file sealed class FakePaymentAuditLogWriter : IPaymentAuditLogWriter
{
    public Task AppendAsync(PaymentAuditEntry entry, CancellationToken ct = default) => Task.CompletedTask;
}

file sealed class FakeUnitOfWork : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
}

file sealed class FakeCorrelationContext : ICorrelationContext
{
    public string CorrelationId => "test-correlation-id";

    public void Set(string correlationId) { }

    public IDisposable Push(string correlationId) => new NoOpDisposable();

    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose() { }
    }
}

file sealed class FakePaymentClientMetrics : IPaymentClientMetrics
{
    public void RecordPaymentSucceeded(long amountCents, string currency) { }

    public void RecordPlatformFee(long feeCents, string currency) { }

    public void RecordConnectOnboardingCompleted() { }

    public void RecordPaymentLinkCreated() { }

    public void RecordPaymentLinkUsed() { }

    public void RecordRefund(string provider) { }

    public void RecordWebhookReceived(string provider) { }

    public void RecordWebhookDuplicate(string provider) { }

    public void RecordWebhookSignatureFailed(string provider) { }
}

file sealed class NoOpLogger<T> : ILogger<T>
{
    public static readonly NoOpLogger<T> Instance = new();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => false;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    ) { }
}

file sealed class FakeMessageBus : IMessageBus
{
    public List<object> Published { get; } = [];

    public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
    {
        if (message is not null)
            Published.Add(message);

        return ValueTask.CompletedTask;
    }

    public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) => throw new NotImplementedException();

    public ValueTask BroadcastToTopicAsync(string topicName, object message, DeliveryOptions? options = null) =>
        throw new NotImplementedException();

    public IReadOnlyList<Envelope> PreviewSubscriptions(object message) => throw new NotImplementedException();

    public IReadOnlyList<Envelope> PreviewSubscriptions(object message, DeliveryOptions options) =>
        throw new NotImplementedException();

    public IDestinationEndpoint EndpointFor(string endpointName) => throw new NotImplementedException();

    public IDestinationEndpoint EndpointFor(Uri uri) => throw new NotImplementedException();

    public Task InvokeForTenantAsync(
        string tenantId,
        object message,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotImplementedException();

    public Task<T> InvokeForTenantAsync<T>(
        string tenantId,
        object message,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotImplementedException();

    public string? TenantId { get; set; }

    public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) =>
        throw new NotImplementedException();

    public Task InvokeAsync(
        object message,
        DeliveryOptions options,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotImplementedException();

    public Task<T> InvokeAsync<T>(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) =>
        throw new NotImplementedException();

    public Task<T> InvokeAsync<T>(
        object message,
        DeliveryOptions options,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotImplementedException();

    public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
        object message,
        CancellationToken cancellation = default
    ) => throw new NotImplementedException();

    public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
        object message,
        DeliveryOptions options,
        CancellationToken cancellation = default
    ) => throw new NotImplementedException();
}
