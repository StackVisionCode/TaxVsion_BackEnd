using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ReconcileHostedCheckout;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Tests.TestDoubles;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>
/// Reconcile al return del hosted checkout — mismo patrón que el de onboarding pero genérico para
/// todos los types post-onboarding (SubscriptionRenewalCheckout, AddOnPurchaseCharge, SeatsPurchaseCharge,
/// PlanChangeCheckout). Cubre: happy path PayPal capture, idempotencia, type no reconciliable, guard
/// cross-tenant, status no terminal.
/// </summary>
public sealed class ReconcileHostedCheckoutHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Target = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task SubscriptionRenewalCheckout_PayPal_captura_marca_succeeded_y_publica_evento_de_renewal()
    {
        var payment = CreateProcessingPayment(SaaSPaymentType.SubscriptionRenewalCheckout);
        var provider = new FakePaymentProvider(
            PaymentProviderCode.PayPal,
            new ChargeAuthorizationResult("CAPTURE-999", PaymentStatus.Succeeded)
        );
        var unitOfWork = new FakeUnitOfWork();

        var result = await ReconcileHostedCheckoutHandler.Handle(
            new ReconcileHostedCheckoutCommand(payment.Id, TenantId),
            new FakeSaaSPaymentRepository(payment),
            new FakePaymentAdapterFactory(provider),
            unitOfWork,
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        var emitted = await SaaSPaymentEventReplay.DispatchAsync(payment);
        Assert.True(result.IsSuccess);
        Assert.Equal(nameof(PaymentStatus.Succeeded), result.Value.Status);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal("CAPTURE-999", payment.ExternalChargeReference!.Value);
        Assert.Equal(1, unitOfWork.SaveChangesCount);

        // El publisher central mapea por type → debe emitir el evento específico de renewal checkout.
        Assert.Contains(emitted, m => m is SubscriptionRenewalCheckoutPaidIntegrationEvent);
        // Y también el "pídase recibo" común a todos los cobros confirmados de un tenant real.
        Assert.Contains(emitted, m => m is SaaSPaymentSucceededIntegrationEvent);
    }

    [Fact]
    public async Task AddOnPurchaseCharge_PayPal_captura_y_publica_evento_de_addon()
    {
        var payment = CreateProcessingPayment(SaaSPaymentType.AddOnPurchaseCharge);
        var provider = new FakePaymentProvider(
            PaymentProviderCode.PayPal,
            new ChargeAuthorizationResult("CAPTURE-ADDON", PaymentStatus.Succeeded)
        );

        var result = await ReconcileHostedCheckoutHandler.Handle(
            new ReconcileHostedCheckoutCommand(payment.Id, TenantId),
            new FakeSaaSPaymentRepository(payment),
            new FakePaymentAdapterFactory(provider),
            new FakeUnitOfWork(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        var emitted = await SaaSPaymentEventReplay.DispatchAsync(payment);
        Assert.True(result.IsSuccess);
        Assert.Contains(emitted, m => m is AddOnCheckoutPaidIntegrationEvent);
    }

    [Fact]
    public async Task Terminal_no_llama_al_provider_ni_guarda_ni_republica()
    {
        var payment = CreateProcessingPayment(SaaSPaymentType.SeatsPurchaseCharge);
        payment.MarkSucceeded(DateTime.UtcNow, Guid.Empty);

        var provider = new ThrowingProvider();
        var unitOfWork = new FakeUnitOfWork();

        var result = await ReconcileHostedCheckoutHandler.Handle(
            new ReconcileHostedCheckoutCommand(payment.Id, TenantId),
            new FakeSaaSPaymentRepository(payment),
            new FakePaymentAdapterFactory(provider),
            unitOfWork,
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        var emitted = await SaaSPaymentEventReplay.DispatchAsync(payment);
        Assert.True(result.IsSuccess);
        Assert.Equal(nameof(PaymentStatus.Succeeded), result.Value.Status);
        Assert.Equal(0, provider.Calls);
        Assert.Equal(0, unitOfWork.SaveChangesCount);
        Assert.Empty(emitted);
    }

    [Fact]
    public async Task Type_no_reconciliable_devuelve_error_sin_llamar_provider()
    {
        var provider = new ThrowingProvider();
        // Un OnboardingInitial tiene su propio endpoint (ReconcileOnboardingCheckout), este rechaza.
        var payment = SaaSPayment
            .CreateForOnboarding(
                Guid.NewGuid(),
                IdempotencyKey.Create("onb-key").Value,
                Money.Create(2900, "USD").Value,
                Guid.NewGuid(),
                PaymentProviderCode.PayPal,
                StatementDescriptor.Create("TAXVISION").Value,
                DateTime.UtcNow
            )
            .Value;
        payment.RecordHostedCheckoutSession(
            "ORDER-X",
            ExternalPaymentReference.Create(PaymentProviderCode.PayPal, "ORDER-X").Value,
            "https://paypal.test/x",
            DateTime.UtcNow
        );

        var result = await ReconcileHostedCheckoutHandler.Handle(
            new ReconcileHostedCheckoutCommand(payment.Id, Guid.Empty),
            new FakeSaaSPaymentRepository(payment, acceptEmptyTenant: true),
            new FakePaymentAdapterFactory(provider),
            new FakeUnitOfWork(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SaaSPayment.NotReconcilable", result.Error.Code);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Pago_de_otro_tenant_devuelve_NotFound()
    {
        var payment = CreateProcessingPayment(SaaSPaymentType.AddOnPurchaseCharge);
        var otroTenant = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var provider = new ThrowingProvider();

        var result = await ReconcileHostedCheckoutHandler.Handle(
            new ReconcileHostedCheckoutCommand(payment.Id, otroTenant),
            // Repo que filtra por tenant: no devuelve el pago de otro.
            new FakeSaaSPaymentRepository(payment),
            new FakePaymentAdapterFactory(provider),
            new FakeUnitOfWork(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SaaSPayment.NotFound", result.Error.Code);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Processing_no_terminal_no_guarda_ni_publica_y_el_pago_queda_abierto()
    {
        var payment = CreateProcessingPayment(SaaSPaymentType.SubscriptionRenewalCheckout);
        var provider = new FakePaymentProvider(
            PaymentProviderCode.PayPal,
            new ChargeAuthorizationResult("ORDER-123", PaymentStatus.Processing)
        );
        var unitOfWork = new FakeUnitOfWork();

        var result = await ReconcileHostedCheckoutHandler.Handle(
            new ReconcileHostedCheckoutCommand(payment.Id, TenantId),
            new FakeSaaSPaymentRepository(payment),
            new FakePaymentAdapterFactory(provider),
            unitOfWork,
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        var emitted = await SaaSPaymentEventReplay.DispatchAsync(payment);
        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.Equal(0, unitOfWork.SaveChangesCount);
        Assert.Empty(emitted);
    }

    [Fact]
    public async Task Provider_succeeded_on_cancelled_payment_flags_for_review()
    {
        var payment = CreateProcessingPayment(SaaSPaymentType.SeatsPurchaseCharge);
        payment.MarkCancelled("BuyerCancelled", DateTime.UtcNow);
        // Solo consulta de estado: sobre un pago cerrado nunca se captura (PayPal capturaría en Finalize).
        var provider = new StatusOnlyProvider(new ChargeAuthorizationResult("CAPTURE-LATE", PaymentStatus.Succeeded));
        var unitOfWork = new FakeUnitOfWork();
        var metrics = new TestDoubles.FakePaymentAppMetrics();

        var result = await ReconcileHostedCheckoutHandler.Handle(
            new ReconcileHostedCheckoutCommand(payment.Id, TenantId),
            new FakeSaaSPaymentRepository(payment),
            new FakePaymentAdapterFactory(provider),
            unitOfWork,
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None,
            metrics: metrics
        );

        var emitted = await SaaSPaymentEventReplay.DispatchAsync(payment);
        Assert.True(result.IsSuccess);
        Assert.Equal(nameof(PaymentStatus.Cancelled), result.Value.Status);
        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.NotNull(payment.ReviewRequiredAtUtc);
        Assert.Equal("LateProviderSuccess", payment.ReviewReasonCode);
        Assert.Equal("CAPTURE-LATE", payment.ReviewProviderReference);
        Assert.Equal(1, unitOfWork.SaveChangesCount);
        Assert.Empty(emitted);
        Assert.Single(metrics.ReviewRequired);
    }

    [Fact]
    public async Task Cancelled_payment_without_provider_success_is_left_alone()
    {
        var payment = CreateProcessingPayment(SaaSPaymentType.SeatsPurchaseCharge);
        payment.MarkCancelled("BuyerCancelled", DateTime.UtcNow);
        var provider = new StatusOnlyProvider(new ChargeAuthorizationResult("ORDER-123", PaymentStatus.Failed));
        var unitOfWork = new FakeUnitOfWork();

        var result = await ReconcileHostedCheckoutHandler.Handle(
            new ReconcileHostedCheckoutCommand(payment.Id, TenantId),
            new FakeSaaSPaymentRepository(payment),
            new FakePaymentAdapterFactory(provider),
            unitOfWork,
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Null(payment.ReviewRequiredAtUtc);
        Assert.Equal(0, unitOfWork.SaveChangesCount);
    }

    // ---------- Fakes ----------

    private static SaaSPayment CreateProcessingPayment(SaaSPaymentType type)
    {
        var payment = SaaSPayment
            .Create(
                TenantId,
                IdempotencyKey.Create($"key-{type}").Value,
                Money.Create(12900, "USD").Value,
                type,
                Target,
                PaymentProviderCode.PayPal,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                Guid.NewGuid(),
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

    private sealed class FakeSaaSPaymentRepository(SaaSPayment payment, bool acceptEmptyTenant = false)
        : ISaaSPaymentRepository
    {
        private readonly bool _seeded = Seed(payment);

        // Lo sembrado se considera ya guardado: sus eventos no cuentan para el comando.
        private static bool Seed(SaaSPayment seeded)
        {
            seeded.ClearDomainEvents();
            return true;
        }

        // El overload con tenantId es el que usa el handler — filtra por tenant.
        public Task<SaaSPayment?> GetByIdAsync(Guid saaSPaymentId, Guid tenantId, CancellationToken ct = default)
        {
            if (payment.Id != saaSPaymentId)
                return Task.FromResult<SaaSPayment?>(null);
            if (!acceptEmptyTenant && payment.TenantId != tenantId)
                return Task.FromResult<SaaSPayment?>(null);
            return Task.FromResult<SaaSPayment?>(payment);
        }

        public Task<SaaSPayment?> GetByIdAsync(Guid saaSPaymentId, CancellationToken ct = default) =>
            Task.FromResult<SaaSPayment?>(payment.Id == saaSPaymentId ? payment : null);

        public Task<SaaSPayment?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SaaSPayment?> GetByExternalReferenceAsync(
            PaymentProviderCode code,
            string providerChargeReference,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<(Guid Id, Guid TenantId)>> GetStalePendingChargeIdsAsync(
            DateTime cutoffUtc,
            int batchSize,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<(Guid Id, Guid TenantId)>>([]);

        public Task<IReadOnlyList<(Guid Id, Guid TenantId)>> GetStuckProcessingIdsAsync(
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

        public Task<SaaSPayment?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SaaSPayment?> GetByProviderCheckoutSessionIdAsync(
            string providerSessionId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<
            IReadOnlyDictionary<(SaaSPaymentType Type, Guid TargetAggregateId), DateTime>
        > GetLatestSucceededCreatedAtByTargetsAsync(
            Guid tenantId,
            IReadOnlyCollection<(SaaSPaymentType Type, Guid TargetAggregateId)> targets,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyDictionary<(SaaSPaymentType, Guid), DateTime>>(
                new Dictionary<(SaaSPaymentType, Guid), DateTime>()
            );

        public Task<IReadOnlyDictionary<SaaSPaymentType, DateTime>> GetLatestSucceededCreatedAtByTypesAsync(
            Guid tenantId,
            IReadOnlyCollection<SaaSPaymentType> types,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyDictionary<SaaSPaymentType, DateTime>>(
                new Dictionary<SaaSPaymentType, DateTime>()
            );

        public Task<(IReadOnlyList<SaaSPayment> Items, int TotalCount)> SearchForTenantAsync(
            Guid tenantId,
            int page,
            int pageSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task AddAsync(SaaSPayment payment, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakePaymentAdapterFactory(IPaymentProvider provider) : IPaymentAdapterFactory
    {
        public IPaymentProvider Resolve(PaymentProviderCode code) =>
            provider.Code == code ? provider : throw new InvalidOperationException();
    }

    private class FakePaymentProvider(PaymentProviderCode code, ChargeAuthorizationResult chargeStatus)
        : IPaymentProvider
    {
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

        public Task<Result<ChargeAuthorizationResult>> FinalizeHostedCheckoutAsync(
            string providerChargeReference,
            Money amount,
            CancellationToken ct
        ) => Task.FromResult(Result.Success(chargeStatus));

        public Task<Result<ChargeLookup>> FindChargeAsync(
            Guid saaSPaymentId,
            DateTime createdAfterUtc,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<Result<ChargeAuthorizationResult>> GetChargeStatusAsync(
            string providerChargeReference,
            CancellationToken ct
        ) => Task.FromResult(Result.Success(chargeStatus));

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
            string idempotencyKey,
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

    // Reimplementa IPaymentProvider: sin eso el handler, que llama por la interfaz, llegaría al método base.
    private sealed class ThrowingProvider() : FakePaymentProvider(PaymentProviderCode.PayPal, Default), IPaymentProvider
    {
        private static readonly ChargeAuthorizationResult Default = new("unused", PaymentStatus.Processing);

        public int Calls { get; private set; }

        public new Task<Result<ChargeAuthorizationResult>> FinalizeHostedCheckoutAsync(
            string providerChargeReference,
            Money amount,
            CancellationToken ct
        ) => Fail();

        public new Task<Result<ChargeAuthorizationResult>> GetChargeStatusAsync(
            string providerChargeReference,
            CancellationToken ct
        ) => Fail();

        private Task<Result<ChargeAuthorizationResult>> Fail()
        {
            Calls++;
            throw new InvalidOperationException("Provider should not be called.");
        }
    }

    // Responde el estado del cobro; Finalize (que en PayPal captura) no debe llamarse.
    private sealed class StatusOnlyProvider(ChargeAuthorizationResult chargeStatus)
        : FakePaymentProvider(PaymentProviderCode.PayPal, chargeStatus),
            IPaymentProvider
    {
        public new Task<Result<ChargeAuthorizationResult>> FinalizeHostedCheckoutAsync(
            string providerChargeReference,
            Money amount,
            CancellationToken ct
        ) => throw new InvalidOperationException("A closed payment must not be finalized (captured).");
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCount++;
            return Task.FromResult(1);
        }
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
