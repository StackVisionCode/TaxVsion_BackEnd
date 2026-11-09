using System.Net;
using System.Reflection;
using System.Text;
using BuildingBlocks.Caching;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stripe;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ClaimSaaSPaymentCharge;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ExecuteSaaSPaymentCharge;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ReconcilePendingSaaSPayment;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.ProviderCustomers;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Infrastructure.Providers.Intellipay;
using TaxVision.PaymentApp.Infrastructure.Providers.Stripe;
using TaxVision.PaymentApp.Tests.TestDoubles;
using Wolverine.Attributes;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>PAY-5: el paso que llama al provider y la reconciliación de lo que quedó sin resultado.</summary>
public sealed class ExecuteSaaSPaymentChargeHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly CountingCharger _charger = new();

    [Fact]
    public void Provider_call_runs_outside_a_transaction()
    {
        var handle = typeof(ExecuteSaaSPaymentChargeHandler).GetMethod(nameof(ExecuteSaaSPaymentChargeHandler.Handle))!;

        Assert.NotNull(handle.GetCustomAttribute<NonTransactionalAttribute>());
    }

    [Fact]
    public async Task Already_resolved_payment_does_not_charge_again()
    {
        var payment = PendingRenewal();
        payment.MarkProcessing(Reference(), "Succeeded", null, Guid.Empty, DateTime.UtcNow);
        payment.MarkSucceeded(DateTime.UtcNow, Guid.Empty);

        await RunAsync(payment);

        Assert.Equal(0, _charger.Calls);
    }

    // Con el resultado ya registrado la clave cambió: llamar de nuevo sería un cobro nuevo.
    [Fact]
    public async Task Payment_with_a_recorded_attempt_is_not_charged_with_a_new_key()
    {
        var payment = PendingRenewal();
        payment.MarkProcessing(Reference(), "processing", null, Guid.Empty, DateTime.UtcNow);

        await RunAsync(payment);

        Assert.Equal(0, _charger.Calls);
    }

    [Fact]
    public async Task Same_pending_attempt_reuses_the_same_provider_key()
    {
        var payment = PendingRenewal();
        var options = Options.Create(new SaaSPaymentChargeOptions());

        var first = await ClaimAsync(payment, options);
        var second = await ClaimAsync(payment, options);

        Assert.Equal(first.Value!.ProviderKey, second.Value!.ProviderKey);
        Assert.Equal(payment.IdempotencyKey, first.Value.ProviderKey);
    }

    // El provider ya olvidó la clave: repetir la llamada podría cobrar dos veces.
    [Fact]
    public async Task Attempt_past_the_provider_key_window_is_not_charged()
    {
        var payment = PendingRenewal();
        payment.MarkChargeAttempted(DateTime.UtcNow.AddHours(-30));

        var claim = await ClaimAsync(payment, Options.Create(new SaaSPaymentChargeOptions()));

        Assert.True(claim.IsSuccess);
        Assert.Null(claim.Value);
    }

    [Fact]
    public async Task Pending_never_attempted_fails_with_retry_without_asking_the_provider()
    {
        var payment = PendingRenewal(ageMinutes: 60);
        var provider = new FakePaymentProvider { Lookup = new ChargeLookup(ChargeLookupOutcome.NotFound) };

        await ReconcileAsync(payment, provider);

        Assert.Equal(0, provider.Lookups);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.NotNull(payment.NextRetryAtUtc);
    }

    [Fact]
    public async Task Pending_attempted_and_charged_at_the_provider_is_recovered()
    {
        var payment = AttemptedRenewal();
        var provider = new FakePaymentProvider
        {
            Lookup = new ChargeLookup(
                ChargeLookupOutcome.Found,
                new ChargeAuthorizationResult("pi_recovered", PaymentStatus.Succeeded)
            ),
        };

        await ReconcileAsync(payment, provider);

        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal("pi_recovered", payment.ExternalChargeReference!.Value);
    }

    [Fact]
    public async Task Pending_attempted_without_a_charge_goes_to_dunning()
    {
        var payment = AttemptedRenewal();

        await ReconcileAsync(
            payment,
            new FakePaymentProvider { Lookup = new ChargeLookup(ChargeLookupOutcome.NotFound) }
        );

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal("SaaSPayment.ChargeNotConfirmed", payment.FailureCode);
        Assert.NotNull(payment.NextRetryAtUtc);
    }

    // Regla de BB-8: sin poder preguntar no se reenvía ni se da por fallido; lo decide una persona.
    [Fact]
    public async Task Pending_attempted_with_a_provider_that_cannot_search_goes_to_review()
    {
        var payment = AttemptedRenewal();

        await ReconcileAsync(
            payment,
            new FakePaymentProvider { Lookup = new ChargeLookup(ChargeLookupOutcome.NotSupported) }
        );

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(SaaSPayment.ReviewReasonChargeOutcomeUnknown, payment.ReviewReasonCode);
        Assert.Null(payment.NextRetryAtUtc);

        var claim = await ClaimAsync(payment, Options.Create(new SaaSPaymentChargeOptions()));
        Assert.Null(claim.Value);
    }

    [Fact]
    public async Task Pending_with_a_command_still_running_is_left_alone()
    {
        var payment = PendingRenewal();
        payment.MarkChargeAttempted(DateTime.UtcNow);
        var provider = new FakePaymentProvider { Lookup = new ChargeLookup(ChargeLookupOutcome.NotFound) };

        await ReconcileAsync(payment, provider);

        Assert.Equal(0, provider.Lookups);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
    }

    // D-PAY-13: el cobro pudo salir. Ni Failed ni dunning con clave nueva: lo resuelve la reconciliación.
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "api_error", null)]
    [InlineData(HttpStatusCode.BadGateway, null, null)]
    [InlineData(HttpStatusCode.BadRequest, "idempotency_error", null)]
    [InlineData(HttpStatusCode.Conflict, "invalid_request_error", "idempotency_key_in_use")]
    public async Task Stripe_unknown_outcome_off_session_is_not_retried_with_a_new_key(
        HttpStatusCode status,
        string? type,
        string? code
    )
    {
        var payment = PendingRenewal();
        var key = payment.ProviderIdempotencyKeyForNextAttempt();
        var error = new StripeException(status, new StripeError { Type = type, Code = code }, "provider error");

        await RunOffSessionAsync(payment, (_, _, _) => Task.FromException<PaymentIntent>(error));

        AssertLeftForReconciliation(payment, key);
    }

    [Fact]
    public async Task Stripe_5xx_on_authorize_is_not_retried_with_a_new_key()
    {
        var payment = PendingRenewal();
        var key = payment.ProviderIdempotencyKeyForNextAttempt();

        await RunAuthorizeAsync(
            payment,
            StripeAdapter(HttpStatusCode.InternalServerError, """{"error":{"type":"api_error","message":"boom"}}""")
        );

        AssertLeftForReconciliation(payment, key);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "")]
    [InlineData(HttpStatusCode.OK, "not json")]
    public async Task Intellipay_unknown_outcome_is_not_retried_with_a_new_key(HttpStatusCode status, string body)
    {
        var payment = PendingRenewal(provider: PaymentProviderCode.Intellipay);
        var key = payment.ProviderIdempotencyKeyForNextAttempt();

        await RunAuthorizeAsync(payment, IntellipayAdapter(status, body));

        AssertLeftForReconciliation(payment, key);
    }

    [Fact]
    public async Task Intellipay_decline_still_goes_to_dunning()
    {
        var payment = PendingRenewal(provider: PaymentProviderCode.Intellipay);

        await RunAuthorizeAsync(
            payment,
            IntellipayAdapter(HttpStatusCode.OK, """{"status":"0","response":"declined","message":"Declined"}""")
        );

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.NotNull(payment.NextRetryAtUtc);
    }

    // Un rechazo definitivo no cobró: sigue con su dunning de siempre.
    [Theory]
    [InlineData(HttpStatusCode.PaymentRequired, "card_error", "card_declined")]
    [InlineData(HttpStatusCode.TooManyRequests, "invalid_request_error", "rate_limit")]
    public async Task Stripe_definitive_failure_still_goes_to_dunning(HttpStatusCode status, string type, string code)
    {
        var payment = PendingRenewal();
        var error = new StripeException(status, new StripeError { Type = type, Code = code }, "provider error");

        await RunOffSessionAsync(payment, (_, _, _) => Task.FromException<PaymentIntent>(error));

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.NotNull(payment.NextRetryAtUtc);
    }

    [Fact]
    public async Task Off_session_intent_still_processing_waits_for_the_provider()
    {
        var payment = PendingRenewal();

        await RunOffSessionAsync(
            payment,
            (_, _, _) => Task.FromResult(new PaymentIntent { Id = "pi_processing", Status = "processing" })
        );

        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.Equal("pi_processing", payment.ExternalChargeReference!.Value);
        Assert.Null(payment.NextRetryAtUtc);
    }

    private static void AssertLeftForReconciliation(SaaSPayment payment, IdempotencyKey key)
    {
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Null(payment.NextRetryAtUtc);
        Assert.Null(payment.FailureCode);
        Assert.Empty(payment.Attempts);
        Assert.NotNull(payment.ChargeAttemptedAtUtc);
        Assert.Equal(key, payment.ProviderIdempotencyKeyForNextAttempt());
    }

    private async Task RunOffSessionAsync(
        SaaSPayment payment,
        Func<PaymentIntentCreateOptions, RequestOptions, CancellationToken, Task<PaymentIntent>> create
    )
    {
        var reference = ProviderCustomerReference.Create(PaymentProviderCode.Stripe, "cus_pay13").Value;
        var customer = TenantProviderCustomer
            .Register(_tenantId, PaymentProviderCode.Stripe, reference, "payer@example.com", DateTime.UtcNow)
            .Value;
        var charger = new StripeOffSessionCharger(
            create,
            new SingleCustomerRepository(customer),
            NullLogger<StripeOffSessionCharger>.Instance,
            new FakePaymentAppMetrics()
        );

        var bus = new CapturingMessageBus();
        bus.Published.Add(Command(payment, SaaSPaymentChargeStrategy.OffSession));
        await TestChargeExecution.DrainAsync(
            bus,
            new FakeSaaSPaymentRepository { ById = payment },
            new SingleChargerFactory(charger)
        );
    }

    private async Task RunAuthorizeAsync(SaaSPayment payment, IPaymentProvider provider)
    {
        var bus = new CapturingMessageBus();
        bus.Published.Add(Command(payment, SaaSPaymentChargeStrategy.Authorize));
        await TestChargeExecution.DrainAsync(
            bus,
            new FakeSaaSPaymentRepository { ById = payment },
            adapters: new FakePaymentAdapterFactory(provider)
        );
    }

    private ExecuteSaaSPaymentChargeCommand Command(SaaSPayment payment, SaaSPaymentChargeStrategy strategy) =>
        new(_tenantId, payment.Id, strategy, "pm_card", "cus_pay13", Guid.Empty, null);

    private static StripePaymentAdapter StripeAdapter(HttpStatusCode status, string body) =>
        new(
            new StripeClient(
                "sk_test_dummy",
                httpClient: new SystemNetHttpClient(
                    new HttpClient(new FixedResponse(status, body)),
                    maxNetworkRetries: 0
                )
            ),
            NullLogger<StripePaymentAdapter>.Instance,
            new FakePaymentAppMetrics()
        );

    private static IntellipayAdapter IntellipayAdapter(HttpStatusCode status, string body)
    {
        var options = new IntellipayOptions
        {
            MerchantKey = "merchant",
            ApiKey = "key",
            BaseUrl = "https://intellipay.test/webapi.cfc",
        };
        var http = new HttpClient(new FixedResponse(status, body));
        var gateway = new IntellipayGateway(
            http,
            Options.Create(options),
            new PassThroughCache(),
            NullLogger<IntellipayGateway>.Instance
        );
        return new IntellipayAdapter(gateway, NullLogger<IntellipayAdapter>.Instance);
    }

    private async Task RunAsync(SaaSPayment payment)
    {
        var bus = new CapturingMessageBus();
        bus.Published.Add(
            new ExecuteSaaSPaymentChargeCommand(
                _tenantId,
                payment.Id,
                SaaSPaymentChargeStrategy.OffSession,
                "pm_card",
                null,
                Guid.Empty,
                null
            )
        );

        await TestChargeExecution.DrainAsync(
            bus,
            new FakeSaaSPaymentRepository { ById = payment },
            new SingleChargerFactory(_charger)
        );
    }

    private Task<Result<SaaSPaymentChargeAttempt?>> ClaimAsync(
        SaaSPayment payment,
        IOptions<SaaSPaymentChargeOptions> options
    ) =>
        ClaimSaaSPaymentChargeHandler.Handle(
            new ClaimSaaSPaymentChargeCommand(_tenantId, payment.Id),
            new FakeSaaSPaymentRepository { ById = payment },
            new FakeUnitOfWork(),
            options,
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

    private Task<Result> ReconcileAsync(SaaSPayment payment, IPaymentProvider provider) =>
        ReconcilePendingSaaSPaymentHandler.Handle(
            new ReconcilePendingSaaSPaymentCommand(_tenantId, payment.Id),
            new FakeSaaSPaymentRepository { ById = payment },
            new FakePaymentAdapterFactory(provider),
            new FakeUnitOfWork(),
            new FakePaymentAppMetrics(),
            Options.Create(new SaaSPaymentChargeOptions()),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

    private SaaSPayment AttemptedRenewal()
    {
        var payment = PendingRenewal(ageMinutes: 60);
        payment.MarkChargeAttempted(DateTime.UtcNow.AddMinutes(-60));
        return payment;
    }

    private SaaSPayment PendingRenewal(int ageMinutes = 0, PaymentProviderCode provider = PaymentProviderCode.Stripe) =>
        SaaSPayment
            .Create(
                _tenantId,
                IdempotencyKey.Create($"pay5-{Guid.NewGuid():N}").Value,
                Money.Create(4900, "USD").Value,
                SaaSPaymentType.SubscriptionRenewal,
                Guid.NewGuid(),
                provider,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                Guid.Empty,
                DateTime.UtcNow.AddMinutes(-ageMinutes)
            )
            .Value;

    private static ExternalPaymentReference Reference() =>
        ExternalPaymentReference.Create(PaymentProviderCode.Stripe, $"pi_{Guid.NewGuid():N}").Value;

    private sealed class CountingCharger : IOffSessionCharger
    {
        public int Calls { get; private set; }
        public string ProviderCode => PaymentProviderCode.Stripe.ToString();

        public Task<Result<OffSessionChargeOutcome>> ChargeAsync(OffSessionChargeRequest request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(
                Result.Success(new OffSessionChargeOutcome(OffSessionChargeStatus.Succeeded, "pi_x", null, null))
            );
        }
    }

    private sealed class SingleChargerFactory(IOffSessionCharger charger) : IOffSessionChargerFactory
    {
        public IOffSessionCharger? For(string providerCode) => charger;
    }

    private sealed class FixedResponse(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(
                new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
            );
    }

    private sealed class SingleCustomerRepository(TenantProviderCustomer customer) : ITenantProviderCustomerRepository
    {
        public Task<TenantProviderCustomer?> GetByTenantAndProviderAsync(
            Guid tenantId,
            PaymentProviderCode code,
            CancellationToken ct = default
        ) => Task.FromResult<TenantProviderCustomer?>(customer);

        public Task<TenantProviderCustomer?> GetByIdAsync(
            Guid tenantProviderCustomerId,
            Guid tenantId,
            CancellationToken ct = default
        ) => Task.FromResult<TenantProviderCustomer?>(customer);

        public Task<IReadOnlyList<TenantProviderCustomer>> GetWithMethodsExpiringBeforeAsync(
            DateTime cutoffUtc,
            int batchSize,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<TenantProviderCustomer>>([]);

        public Task AddAsync(TenantProviderCustomer customer, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class PassThroughCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) => Task.FromResult(default(T));

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;

        public Task<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            TimeSpan? ttl = null,
            CancellationToken ct = default
        ) => factory(ct);
    }
}
