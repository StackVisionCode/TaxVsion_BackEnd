using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.OnboardingCheckouts.Commands;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;

namespace TaxVision.PaymentApp.Tests.Application;

public sealed class ReconcileOnboardingCheckoutHandlerTests
{
    [Fact]
    public async Task Succeeded_status_reconciles_reference_publishes_onboarding_event_and_saves()
    {
        var payment = CreateProcessingOnboardingPayment();
        var provider = new FakePaymentProvider(
            PaymentProviderCode.PayPal,
            new ChargeAuthorizationResult("CAPTURE-123", PaymentStatus.Succeeded)
        );
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        var result = await ReconcileOnboardingCheckoutHandler.Handle(
            new ReconcileOnboardingCheckoutCommand(payment.Id),
            new FakeSaaSPaymentRepository(payment),
            new FakePaymentAdapterFactory(provider),
            unitOfWork,
            new FakeCorrelationContext(),
            bus,
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(OnboardingPaymentStatus.Succeeded, result.Value.Status);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal("CAPTURE-123", payment.ExternalChargeReference!.Value);
        Assert.Equal(1, unitOfWork.SaveChangesCount);

        var published = Assert.IsType<OnboardingPaymentSucceededIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(payment.Id, published.SaaSPaymentId);
        Assert.Equal("CAPTURE-123", published.ProviderPaymentReference);
    }

    [Fact]
    public async Task Processing_status_keeps_payment_open_without_saving_or_publishing()
    {
        var payment = CreateProcessingOnboardingPayment();
        var provider = new FakePaymentProvider(
            PaymentProviderCode.PayPal,
            new ChargeAuthorizationResult("ORDER-123", PaymentStatus.Processing)
        );
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        var result = await ReconcileOnboardingCheckoutHandler.Handle(
            new ReconcileOnboardingCheckoutCommand(payment.Id),
            new FakeSaaSPaymentRepository(payment),
            new FakePaymentAdapterFactory(provider),
            unitOfWork,
            new FakeCorrelationContext(),
            bus,
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(OnboardingPaymentStatus.Processing, result.Value.Status);
        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.Equal(0, unitOfWork.SaveChangesCount);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Terminal_payment_replays_without_calling_provider()
    {
        var payment = CreateProcessingOnboardingPayment();
        payment.MarkSucceeded(DateTime.UtcNow, Guid.Empty);
        var provider = new ThrowingPaymentProvider(PaymentProviderCode.PayPal);

        var result = await ReconcileOnboardingCheckoutHandler.Handle(
            new ReconcileOnboardingCheckoutCommand(payment.Id),
            new FakeSaaSPaymentRepository(payment),
            new FakePaymentAdapterFactory(provider),
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            new FakeMessageBus(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(OnboardingPaymentStatus.Succeeded, result.Value.Status);
    }

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

    private sealed class FakeSaaSPaymentRepository(SaaSPayment payment) : ISaaSPaymentRepository
    {
        public Task<SaaSPayment?> GetByIdAsync(Guid saaSPaymentId, CancellationToken ct = default) =>
            Task.FromResult(payment.Id == saaSPaymentId ? payment : null);

        public Task<SaaSPayment?> GetByIdAsync(Guid saaSPaymentId, Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SaaSPayment?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default) =>
            throw new NotSupportedException();

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

        public Task<Result<ChargeAuthorizationResult>> GetChargeStatusAsync(
            string providerChargeReference,
            CancellationToken ct
        ) => Task.FromResult(Result.Success(chargeStatus));

        public Task<Result<ChargeAuthorizationResult>> FinalizeHostedCheckoutAsync(
            string providerChargeReference,
            Money amount,
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

    private sealed class ThrowingPaymentProvider(PaymentProviderCode code)
        : FakePaymentProvider(code, new ChargeAuthorizationResult("unused", PaymentStatus.Processing))
    {
        public new Task<Result<ChargeAuthorizationResult>> GetChargeStatusAsync(
            string providerChargeReference,
            CancellationToken ct
        ) => throw new InvalidOperationException("Provider should not be called for terminal payments.");

        public new Task<Result<ChargeAuthorizationResult>> FinalizeHostedCheckoutAsync(
            string providerChargeReference,
            Money amount,
            CancellationToken ct
        ) => throw new InvalidOperationException("Provider should not be called for terminal payments.");
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
