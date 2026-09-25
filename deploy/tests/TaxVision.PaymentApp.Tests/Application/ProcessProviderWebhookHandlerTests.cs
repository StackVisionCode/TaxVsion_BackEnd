using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ProcessProviderWebhook;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Domain.Webhooks;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;

namespace TaxVision.PaymentApp.Tests.Application;

public sealed class ProcessProviderWebhookHandlerTests
{
    [Fact]
    public async Task PayPal_capture_completed_applies_payment_reconciles_capture_and_publishes_onboarding_success()
    {
        var payment = CreateProcessingOnboardingPayment();
        var provider = new FakePaymentProvider(
            PaymentProviderCode.PayPal,
            new WebhookVerificationResult("paypal-event-1", "PAYMENT.CAPTURE.COMPLETED", "{}"),
            new WebhookEventPayload(
                ProviderChargeReference: "ORDER-123",
                Status: PaymentStatus.Succeeded,
                FailureCode: null,
                FailureMessage: null,
                RefundedAmountCents: null,
                ReconciledChargeReference: "CAPTURE-123"
            )
        );
        var webhooks = new FakeWebhookEventRepository();
        var bus = new FakeMessageBus();

        var result = await ProcessProviderWebhookHandler.Handle(
            new ProcessProviderWebhookCommand(PaymentProviderCode.PayPal, "{}", PayPalHeaders()),
            new FakePaymentAdapterFactory(provider),
            new FakeProviderWebhookSecrets(),
            webhooks,
            new FakeSaaSPaymentRepository(payment),
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(),
            new FakePaymentAppMetrics(),
            new FakePaymentAttemptThrottle(),
            new FakeCorrelationContext(),
            bus,
            NullLogger<WebhookEvent>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal("CAPTURE-123", payment.ExternalChargeReference!.Value);
        Assert.Equal(WebhookEventStatus.Applied, webhooks.Added!.Status);

        var published = Assert.IsType<OnboardingPaymentSucceededIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(payment.OnboardingId, published.OnboardingId);
        Assert.Equal("CAPTURE-123", published.ProviderPaymentReference);
    }

    [Fact]
    public async Task PayPal_order_approved_is_recorded_without_publishing_onboarding_success()
    {
        var payment = CreateProcessingOnboardingPayment();
        var provider = new FakePaymentProvider(
            PaymentProviderCode.PayPal,
            new WebhookVerificationResult("paypal-event-2", "CHECKOUT.ORDER.APPROVED", "{}"),
            new WebhookEventPayload(
                ProviderChargeReference: "ORDER-123",
                Status: PaymentStatus.Processing,
                FailureCode: null,
                FailureMessage: null,
                RefundedAmountCents: null
            )
        );
        var webhooks = new FakeWebhookEventRepository();
        var bus = new FakeMessageBus();

        var result = await ProcessProviderWebhookHandler.Handle(
            new ProcessProviderWebhookCommand(PaymentProviderCode.PayPal, "{}", PayPalHeaders()),
            new FakePaymentAdapterFactory(provider),
            new FakeProviderWebhookSecrets(),
            webhooks,
            new FakeSaaSPaymentRepository(payment),
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(),
            new FakePaymentAppMetrics(),
            new FakePaymentAttemptThrottle(),
            new FakeCorrelationContext(),
            bus,
            NullLogger<WebhookEvent>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.Equal(WebhookEventStatus.Applied, webhooks.Added!.Status);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Duplicate_provider_event_is_idempotent_noop()
    {
        var payment = CreateProcessingOnboardingPayment();
        var provider = new FakePaymentProvider(
            PaymentProviderCode.PayPal,
            new WebhookVerificationResult("paypal-event-duplicate", "PAYMENT.CAPTURE.COMPLETED", "{}"),
            new WebhookEventPayload("ORDER-123", PaymentStatus.Succeeded, null, null, null)
        );
        // El evento ya fue aplicado (estado terminal) en una entrega anterior → se descarta.
        var webhooks = new FakeWebhookEventRepository(
            AppliedEvent(PaymentProviderCode.PayPal, "paypal-event-duplicate")
        );

        var result = await ProcessProviderWebhookHandler.Handle(
            new ProcessProviderWebhookCommand(PaymentProviderCode.PayPal, "{}", PayPalHeaders()),
            new FakePaymentAdapterFactory(provider),
            new FakeProviderWebhookSecrets(),
            webhooks,
            new FakeSaaSPaymentRepository(payment),
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(),
            new FakePaymentAppMetrics(),
            new FakePaymentAttemptThrottle(),
            new FakeCorrelationContext(),
            new FakeMessageBus(),
            NullLogger<WebhookEvent>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Null(webhooks.Added);
        Assert.False(provider.ParseWasCalled);
    }

    [Fact]
    public async Task Reprocesses_a_previously_failed_event_on_provider_retry_so_the_payment_is_not_lost()
    {
        // F1: la entrega anterior insertó la fila pero un fallo transitorio la dejó Failed (no
        // terminal) sin aplicar el cargo. El reintento del provider debe RE-PROCESARLA, no descartarla
        // como duplicado — de lo contrario un pago real se perdería.
        var payment = CreateProcessingOnboardingPayment();
        var provider = new FakePaymentProvider(
            PaymentProviderCode.PayPal,
            new WebhookVerificationResult("paypal-event-retry", "PAYMENT.CAPTURE.COMPLETED", "{}"),
            new WebhookEventPayload(
                ProviderChargeReference: "ORDER-123",
                Status: PaymentStatus.Succeeded,
                FailureCode: null,
                FailureMessage: null,
                RefundedAmountCents: null,
                ReconciledChargeReference: "CAPTURE-123"
            )
        );
        var existing = FailedEvent(PaymentProviderCode.PayPal, "paypal-event-retry");
        var webhooks = new FakeWebhookEventRepository(existing);
        var bus = new FakeMessageBus();

        var result = await ProcessProviderWebhookHandler.Handle(
            new ProcessProviderWebhookCommand(PaymentProviderCode.PayPal, "{}", PayPalHeaders()),
            new FakePaymentAdapterFactory(provider),
            new FakeProviderWebhookSecrets(),
            webhooks,
            new FakeSaaSPaymentRepository(payment),
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(),
            new FakePaymentAppMetrics(),
            new FakePaymentAttemptThrottle(),
            new FakeCorrelationContext(),
            bus,
            NullLogger<WebhookEvent>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.True(provider.ParseWasCalled); // se re-procesó, NO se descartó
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(WebhookEventStatus.Applied, existing.Status); // reusó la fila existente
        Assert.Null(webhooks.Added); // no insertó otra fila
        Assert.Single(bus.Published.OfType<OnboardingPaymentSucceededIntegrationEvent>());
    }

    [Fact]
    public async Task Throttled_webhook_asks_the_provider_to_retry_and_leaves_the_event_reprocessable()
    {
        // Antes: Rejected (terminal) + 200 → el provider no reintentaba y el pago confirmado se perdía.
        var payment = CreateProcessingOnboardingPayment();
        var provider = new FakePaymentProvider(
            PaymentProviderCode.PayPal,
            new WebhookVerificationResult("paypal-event-throttled", "PAYMENT.CAPTURE.COMPLETED", "{}"),
            new WebhookEventPayload("ORDER-123", PaymentStatus.Succeeded, null, null, null)
        );
        var webhooks = new FakeWebhookEventRepository();
        var bus = new FakeMessageBus();

        var result = await ProcessProviderWebhookHandler.Handle(
            new ProcessProviderWebhookCommand(PaymentProviderCode.PayPal, "{}", PayPalHeaders()),
            new FakePaymentAdapterFactory(provider),
            new FakeProviderWebhookSecrets(),
            webhooks,
            new FakeSaaSPaymentRepository(payment),
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(),
            new FakePaymentAppMetrics(),
            new FakePaymentAttemptThrottle(webhookThrottled: true),
            new FakeCorrelationContext(),
            bus,
            NullLogger<WebhookEvent>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(ProcessProviderWebhookHandler.WebhookThrottledCode, result.Error.Code);
        Assert.Equal(WebhookEventStatus.Failed, webhooks.Added!.Status);
        Assert.False(webhooks.Added.IsTerminal); // la próxima entrega lo re-procesa
        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Amount_mismatch_on_a_success_event_is_not_applied_and_the_event_is_marked_stale()
    {
        // F2: el pago espera 4900 USD; el provider reporta que se cobró solo 100. Aunque el evento
        // esté firmado, NO debe marcarse Succeeded por un importe distinto.
        var payment = CreateProcessingOnboardingPayment();
        var provider = new FakePaymentProvider(
            PaymentProviderCode.PayPal,
            new WebhookVerificationResult("paypal-event-mismatch", "PAYMENT.CAPTURE.COMPLETED", "{}"),
            new WebhookEventPayload(
                ProviderChargeReference: "ORDER-123",
                Status: PaymentStatus.Succeeded,
                FailureCode: null,
                FailureMessage: null,
                RefundedAmountCents: null,
                ReconciledChargeReference: "CAPTURE-123",
                PaidAmountCents: 100,
                PaidCurrency: "USD"
            )
        );
        var webhooks = new FakeWebhookEventRepository();
        var bus = new FakeMessageBus();

        var result = await ProcessProviderWebhookHandler.Handle(
            new ProcessProviderWebhookCommand(PaymentProviderCode.PayPal, "{}", PayPalHeaders()),
            new FakePaymentAdapterFactory(provider),
            new FakeProviderWebhookSecrets(),
            webhooks,
            new FakeSaaSPaymentRepository(payment),
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(),
            new FakePaymentAppMetrics(),
            new FakePaymentAttemptThrottle(),
            new FakeCorrelationContext(),
            bus,
            NullLogger<WebhookEvent>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess); // no reintenta en bucle: se registra y no se aplica
        Assert.Equal(PaymentStatus.Processing, payment.Status); // NO quedó Succeeded
        Assert.Equal(WebhookEventStatus.Stale, webhooks.Added!.Status);
        Assert.Empty(bus.Published); // no se publicó éxito de onboarding
    }

    [Fact]
    public async Task Matching_amount_on_a_success_event_is_applied()
    {
        // F2 (contraparte): monto y moneda coinciden con el cargo → se aplica normalmente.
        var payment = CreateProcessingOnboardingPayment();
        var provider = new FakePaymentProvider(
            PaymentProviderCode.PayPal,
            new WebhookVerificationResult("paypal-event-match", "PAYMENT.CAPTURE.COMPLETED", "{}"),
            new WebhookEventPayload(
                ProviderChargeReference: "ORDER-123",
                Status: PaymentStatus.Succeeded,
                FailureCode: null,
                FailureMessage: null,
                RefundedAmountCents: null,
                ReconciledChargeReference: "CAPTURE-123",
                PaidAmountCents: 4900,
                PaidCurrency: "usd"
            )
        );
        var webhooks = new FakeWebhookEventRepository();

        var result = await ProcessProviderWebhookHandler.Handle(
            new ProcessProviderWebhookCommand(PaymentProviderCode.PayPal, "{}", PayPalHeaders()),
            new FakePaymentAdapterFactory(provider),
            new FakeProviderWebhookSecrets(),
            webhooks,
            new FakeSaaSPaymentRepository(payment),
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(),
            new FakePaymentAppMetrics(),
            new FakePaymentAttemptThrottle(),
            new FakeCorrelationContext(),
            new FakeMessageBus(),
            NullLogger<WebhookEvent>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(WebhookEventStatus.Applied, webhooks.Added!.Status);
    }

    [Fact]
    public async Task Concurrent_duplicate_provider_event_unique_conflict_is_idempotent_noop()
    {
        var payment = CreateProcessingOnboardingPayment();
        var provider = new FakePaymentProvider(
            PaymentProviderCode.PayPal,
            new WebhookVerificationResult("paypal-event-race", "PAYMENT.CAPTURE.COMPLETED", "{}"),
            new WebhookEventPayload("ORDER-123", PaymentStatus.Succeeded, null, null, null)
        );

        var result = await ProcessProviderWebhookHandler.Handle(
            new ProcessProviderWebhookCommand(PaymentProviderCode.PayPal, "{}", PayPalHeaders()),
            new FakePaymentAdapterFactory(provider),
            new FakeProviderWebhookSecrets(),
            new FakeWebhookEventRepository(),
            new FakeSaaSPaymentRepository(payment),
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(
                new ConflictException(
                    "Persistence.UniqueConstraint",
                    "A record with the same unique values already exists."
                )
            ),
            new FakePaymentAppMetrics(),
            new FakePaymentAttemptThrottle(),
            new FakeCorrelationContext(),
            new FakeMessageBus(),
            NullLogger<WebhookEvent>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.False(provider.ParseWasCalled);
    }

    private static IReadOnlyDictionary<string, string> PayPalHeaders() =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PAYPAL-AUTH-ALGO"] = "SHA256withRSA",
            ["PAYPAL-CERT-URL"] = "https://api-m.sandbox.paypal.com/certs/test",
            ["PAYPAL-TRANSMISSION-ID"] = "transmission-id",
            ["PAYPAL-TRANSMISSION-SIG"] = "signature",
            ["PAYPAL-TRANSMISSION-TIME"] = "2026-09-04T12:00:00Z",
        };

    private static SaaSPayment CreateProcessingOnboardingPayment()
    {
        var payment = SaaSPayment
            .CreateForOnboarding(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                IdempotencyKey.Create("paypal-onboarding-key").Value,
                Money.Create(4900, "USD").Value,
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                PaymentProviderCode.PayPal,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                DateTime.UtcNow
            )
            .Value;

        payment.RecordHostedCheckoutSession(
            "ORDER-123",
            ExternalPaymentReference.Create(PaymentProviderCode.PayPal, "ORDER-123").Value,
            "https://paypal.test/checkout",
            DateTime.UtcNow
        );

        return payment;
    }

    private sealed class FakePaymentProvider(
        PaymentProviderCode code,
        WebhookVerificationResult verification,
        WebhookEventPayload payload
    ) : IPaymentProvider
    {
        public bool ParseWasCalled { get; private set; }

        public PaymentProviderCode Code => code;

        public ProviderCapabilities Capabilities { get; } =
            new()
            {
                Code = code,
                DisplayName = code.ToString(),
                SupportsOneShotCharge = true,
                SupportsRecurringCharge = false,
                SupportsHostedCheckoutRedirect = true,
                SupportsInlineElements = false,
                SupportsWebhookSignatureVerification = true,
                SupportedMethods = new HashSet<PaymentMethodKind> { PaymentMethodKind.Wallet },
                SupportsPartialRefund = true,
                Supports3DSecure = false,
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

        public Task<Result<WebhookVerificationResult>> VerifyWebhookSignatureAsync(
            ProviderWebhookVerificationRequest request,
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
        ) => throw new NotSupportedException();
    }

    private sealed class FakePaymentAdapterFactory(IPaymentProvider provider) : IPaymentAdapterFactory
    {
        public IPaymentProvider Resolve(PaymentProviderCode code) =>
            provider.Code == code ? provider : throw new InvalidOperationException();
    }

    private sealed class FakeProviderWebhookSecrets : IProviderWebhookSecrets
    {
        public string? GetWebhookSecret(PaymentProviderCode code) =>
            code == PaymentProviderCode.Stripe ? "stripe-secret" : null;

        public string? GetWebhookId(PaymentProviderCode code) =>
            code == PaymentProviderCode.PayPal ? "paypal-webhook-id" : null;
    }

    private sealed class FakeWebhookEventRepository(WebhookEvent? existing = null) : IWebhookEventRepository
    {
        public WebhookEvent? Added { get; private set; }

        public Task<WebhookEvent?> GetByProviderEventIdAsync(
            PaymentProviderCode code,
            string providerEventId,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                existing is not null && existing.ProviderCode == code && existing.ProviderEventId == providerEventId
                    ? existing
                    : null
            );

        public Task AddAsync(WebhookEvent webhookEvent, CancellationToken ct = default)
        {
            Added = webhookEvent;
            return Task.CompletedTask;
        }
    }

    private static WebhookEvent BuildEvent(
        PaymentProviderCode code,
        string eventId,
        Action<WebhookEvent> transitionToState
    )
    {
        var evt = WebhookEvent
            .Receive(code, eventId, "PAYMENT.CAPTURE.COMPLETED", "{}", "signature-snapshot", DateTime.UtcNow)
            .Value;
        transitionToState(evt);
        return evt;
    }

    private static WebhookEvent AppliedEvent(PaymentProviderCode code, string eventId) =>
        BuildEvent(
            code,
            eventId,
            evt =>
            {
                evt.MarkProcessing(DateTime.UtcNow);
                evt.MarkApplied(null, DateTime.UtcNow);
            }
        );

    private static WebhookEvent FailedEvent(PaymentProviderCode code, string eventId) =>
        BuildEvent(
            code,
            eventId,
            evt =>
            {
                evt.MarkProcessing(DateTime.UtcNow);
                evt.MarkFailed("transient failure on a previous delivery", DateTime.UtcNow);
            }
        );

    private sealed class FakeSaaSPaymentRepository(SaaSPayment payment) : ISaaSPaymentRepository
    {
        public Task<SaaSPayment?> GetByExternalReferenceAsync(
            PaymentProviderCode code,
            string providerChargeReference,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                payment.ProviderCode == code && payment.ExternalChargeReference?.Value == providerChargeReference
                    ? payment
                    : null
            );

        public Task<SaaSPayment?> GetByIdAsync(Guid saaSPaymentId, Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SaaSPayment?> GetByIdAsync(Guid saaSPaymentId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SaaSPayment?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default) =>
            throw new NotSupportedException();

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

        public Task AddAsync(SaaSPayment payment, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakePaymentAuditLogWriter : IPaymentAuditLogWriter
    {
        public Task AppendAsync(PaymentAuditEntry entry, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeUnitOfWork(Exception? firstSaveException = null) : IUnitOfWork
    {
        private Exception? _firstSaveException = firstSaveException;

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            if (_firstSaveException is not null)
            {
                var ex = _firstSaveException;
                _firstSaveException = null;
                throw ex;
            }

            return Task.FromResult(1);
        }
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

    private sealed class FakePaymentAttemptThrottle(bool webhookThrottled = false) : IPaymentAttemptThrottle
    {
        public Task<bool> IsWebhookThrottledAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(webhookThrottled);

        public Task RegisterWebhookAttemptAsync(Guid tenantId, CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> IsAdminActionThrottledAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task RegisterAdminActionAttemptAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeCorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "test-correlation";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new NoopScope();

        private sealed class NoopScope : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class FakeMessageBus : IMessageBus
    {
        public List<object> Published { get; } = [];

        public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
        {
            if (message is not null)
                Published.Add(message);

            return ValueTask.CompletedTask;
        }

        public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) =>
            throw new NotImplementedException();

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

        public Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

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
}
