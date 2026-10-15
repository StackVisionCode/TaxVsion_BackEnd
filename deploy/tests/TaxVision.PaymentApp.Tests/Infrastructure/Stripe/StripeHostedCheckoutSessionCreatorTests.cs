using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.ProviderCustomers;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Infrastructure.Providers.Stripe;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Infrastructure.Stripe;

/// <summary>
/// Verifica que el creator pasa <see cref="HostedCheckoutSessionRequest.StoreMethodForOffSession"/>
/// y el <c>cus_</c> del tenant al adapter cuando se pide guardar off-session, y provisiona lazy
/// el <see cref="TenantProviderCustomer"/> si no existía.
/// </summary>
public sealed class StripeHostedCheckoutSessionCreatorTests
{
    private static readonly Guid TenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SaaSPaymentId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly IOptions<StripeOptions> StripeOpts = Options.Create(
        new StripeOptions { SecretKey = "sk_test_x", WebhookSecret = "whsec_x" }
    );

    [Fact]
    public async Task Passes_setup_future_usage_and_customer_when_store_for_off_session()
    {
        var reference = ProviderCustomerReference.Create(PaymentProviderCode.Stripe, "cus_existing").Value;
        var seeded = TenantProviderCustomer
            .Register(TenantId, PaymentProviderCode.Stripe, reference, "payer@example.com", DateTime.UtcNow)
            .Value;
        var customers = new TrackingCustomerRepo(seeded);
        var provider = new RecordingProvider();
        var creator = new StripeHostedCheckoutSessionCreator(
            new FakePaymentAdapterFactory(provider),
            customers,
            new FakeUnitOfWork(),
            StripeOpts,
            NullLogger<StripeHostedCheckoutSessionCreator>.Instance
        );

        var result = await creator.CreateAsync(BuildRequest(storeForOffSession: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(provider.LastRequest);
        Assert.True(provider.LastRequest!.StoreMethodForOffSession);
        Assert.Equal("cus_existing", provider.LastRequest.ProviderCustomerId);
        Assert.False(customers.Added, "TenantProviderCustomer already existed; no provisioning expected.");
    }

    [Fact]
    public async Task Lazily_provisions_customer_when_none_exists()
    {
        var customers = new TrackingCustomerRepo(null);
        var provider = new RecordingProvider(provisionedCustomer: "cus_new");
        var unitOfWork = new FakeUnitOfWork();
        var creator = new StripeHostedCheckoutSessionCreator(
            new FakePaymentAdapterFactory(provider),
            customers,
            unitOfWork,
            StripeOpts,
            NullLogger<StripeHostedCheckoutSessionCreator>.Instance
        );

        var result = await creator.CreateAsync(BuildRequest(storeForOffSession: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(customers.Added, "Expected a new TenantProviderCustomer to be persisted.");
        Assert.Equal(1, unitOfWork.SaveCalls);
        Assert.Equal("cus_new", provider.LastRequest!.ProviderCustomerId);
    }

    [Fact]
    public async Task Does_not_set_customer_when_store_for_off_session_is_false()
    {
        var customers = new TrackingCustomerRepo(null);
        var provider = new RecordingProvider();
        var creator = new StripeHostedCheckoutSessionCreator(
            new FakePaymentAdapterFactory(provider),
            customers,
            new FakeUnitOfWork(),
            StripeOpts,
            NullLogger<StripeHostedCheckoutSessionCreator>.Instance
        );

        var result = await creator.CreateAsync(BuildRequest(storeForOffSession: false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(provider.LastRequest!.StoreMethodForOffSession);
        Assert.Null(provider.LastRequest.ProviderCustomerId);
    }

    private static HostedCheckoutCreateRequest BuildRequest(bool storeForOffSession) =>
        new(
            TenantId: TenantId,
            SaaSPaymentId: SaaSPaymentId,
            AmountCents: 2999,
            Currency: "USD",
            PayerEmail: "payer@example.com",
            SuccessUrl: "https://app.example.com/ok",
            CancelUrl: "https://app.example.com/cancel",
            IdempotencyKey: "idem-abc",
            Provider: "Stripe",
            Method: "Card",
            StoreMethodForOffSession: storeForOffSession
        );

    private sealed class TrackingCustomerRepo(TenantProviderCustomer? seeded) : ITenantProviderCustomerRepository
    {
        public bool Added { get; private set; }

        public Task<TenantProviderCustomer?> GetByTenantAndProviderAsync(
            Guid tenantId,
            PaymentProviderCode code,
            CancellationToken ct = default
        ) => Task.FromResult(seeded);

        public Task<TenantProviderCustomer?> GetByIdAsync(
            Guid tenantProviderCustomerId,
            Guid tenantId,
            CancellationToken ct = default
        ) => Task.FromResult<TenantProviderCustomer?>(null);

        public Task<IReadOnlyList<TenantProviderCustomer>> GetWithMethodsExpiringBeforeAsync(
            DateTime cutoffUtc,
            int batchSize,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<TenantProviderCustomer>>(Array.Empty<TenantProviderCustomer>());

        public Task AddAsync(TenantProviderCustomer customer, CancellationToken ct = default)
        {
            Added = true;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingProvider(string? provisionedCustomer = null) : IPaymentProvider
    {
        public HostedCheckoutSessionRequest? LastRequest { get; private set; }

        public PaymentProviderCode Code => PaymentProviderCode.Stripe;

        public ProviderCapabilities Capabilities { get; } =
            new()
            {
                Code = PaymentProviderCode.Stripe,
                DisplayName = "Stripe",
                SupportsOneShotCharge = true,
                SupportsRecurringCharge = true,
                SupportsHostedCheckoutRedirect = true,
                SupportsInlineElements = true,
                SupportsWebhookSignatureVerification = true,
                SupportedMethods = new HashSet<PaymentMethodKind> { PaymentMethodKind.Card },
                SupportsPartialRefund = true,
                Supports3DSecure = true,
                SupportsSavedPaymentMethods = true,
                SupportsMultiCurrency = true,
                SupportsMarketplaceConnect = false,
                SupportsIdempotencyKeys = true,
                SupportsCardTokenization = true,
                RequiresCustomerRegistrationBeforeCharge = false,
                SupportedCurrencies = new HashSet<string> { "USD" },
                SupportedCountries = new HashSet<string> { "US" },
                TypicalAuthorizeLatency = TimeSpan.FromMilliseconds(50),
                SuggestedRetryCount = 1,
            };

        public Task<Result<ProviderCustomerToken>> GetOrCreateCustomerAsync(
            Guid tenantId,
            string email,
            string? name,
            CancellationToken ct
        ) =>
            Task.FromResult(
                Result.Success(
                    new ProviderCustomerToken(provisionedCustomer ?? "cus_default", PaymentProviderCode.Stripe)
                )
            );

        public Task<Result<HostedCheckoutSessionResult>> CreateHostedCheckoutSessionAsync(
            HostedCheckoutSessionRequest request,
            CancellationToken ct
        )
        {
            LastRequest = request;
            return Task.FromResult(
                Result.Success(
                    new HostedCheckoutSessionResult("sess_123", "pi_123", "https://checkout.example.com/sess_123")
                )
            );
        }

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
}
