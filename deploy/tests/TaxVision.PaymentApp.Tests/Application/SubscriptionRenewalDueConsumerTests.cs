using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Billing.Abstractions;
using TaxVision.PaymentApp.Application.SaaSPayments.IntegrationEvents;
using TaxVision.PaymentApp.Domain.Billing;
using TaxVision.PaymentApp.Domain.ProviderCustomers;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>Rama off-session vs flow manual segun la proyeccion local de
/// TenantBillingPreference y los metodos guardados en TenantProviderCustomer.</summary>
public sealed class SubscriptionRenewalDueConsumerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _subscriptionId = Guid.NewGuid();

    [Fact]
    public async Task Automatic_with_default_pm_and_active_mandate_charges_off_session_and_publishes_succeeded()
    {
        var charger = new FakeOffSessionCharger(
            new OffSessionChargeOutcome(OffSessionChargeStatus.Succeeded, "pi_test_1", null, null)
        );
        var customers = new FakeProviderCustomers(WithDefaultMethod());
        var preferences = new FakeBillingPreferences(AutomaticWithMandate());
        var payments = new FakeSaaSPaymentRepository();
        var bus = new CapturingMessageBus();

        await SubscriptionRenewalDueConsumer.Handle(
            BuildEvent(),
            bus,
            preferences,
            customers,
            new FakeFactory(charger),
            payments,
            TestPlatform.Settings,
            new FakePaymentAppMetrics(),
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(charger.Called);
        Assert.NotNull(payments.Added);
        Assert.Equal(PaymentStatus.Succeeded, payments.Added!.Status);
        Assert.Contains(bus.Published, m => m is SubscriptionRenewalPaymentSucceededIntegrationEvent);
    }

    [Fact]
    public async Task Automatic_with_revoked_mandate_falls_back_without_calling_charger()
    {
        var charger = new FakeOffSessionCharger(
            new OffSessionChargeOutcome(OffSessionChargeStatus.Succeeded, "pi_x", null, null)
        );
        var customers = new FakeProviderCustomers(WithDefaultMethod());
        // HasActiveMandate=false simula el escenario del mandate revocado proyectado.
        var preferences = new FakeBillingPreferences(
            TenantBillingPreferenceProjection.Create(
                _tenantId,
                TenantBillingPreferenceProjection.ModeAutomatic,
                PaymentProviderCode.Stripe.ToString(),
                hasActiveMandate: false,
                agreedAmountCents: null,
                agreedCurrency: null,
                agreedCadence: null,
                revisionTicks: 1,
                updatedAtUtc: DateTime.UtcNow
            )
        );
        var invoked = new InvokedLegacyBus();

        await SubscriptionRenewalDueConsumer.Handle(
            BuildEvent(),
            invoked,
            preferences,
            customers,
            new FakeFactory(charger),
            new FakeSaaSPaymentRepository(),
            TestPlatform.Settings,
            new FakePaymentAppMetrics(),
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.False(charger.Called);
        Assert.True(invoked.ChargeInvoked);
    }

    [Fact]
    public async Task Manual_mode_falls_back_without_calling_charger()
    {
        var charger = new FakeOffSessionCharger(
            new OffSessionChargeOutcome(OffSessionChargeStatus.Succeeded, "pi_x", null, null)
        );
        var preferences = new FakeBillingPreferences(
            TenantBillingPreferenceProjection.Create(
                _tenantId,
                TenantBillingPreferenceProjection.ModeManual,
                providerCode: null,
                hasActiveMandate: false,
                agreedAmountCents: null,
                agreedCurrency: null,
                agreedCadence: null,
                revisionTicks: 1,
                updatedAtUtc: DateTime.UtcNow
            )
        );
        var invoked = new InvokedLegacyBus();

        await SubscriptionRenewalDueConsumer.Handle(
            BuildEvent(),
            invoked,
            preferences,
            new FakeProviderCustomers(WithDefaultMethod()),
            new FakeFactory(charger),
            new FakeSaaSPaymentRepository(),
            TestPlatform.Settings,
            new FakePaymentAppMetrics(),
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.False(charger.Called);
        Assert.True(invoked.ChargeInvoked);
    }

    [Fact]
    public async Task No_default_payment_method_falls_back_without_calling_charger()
    {
        var charger = new FakeOffSessionCharger(
            new OffSessionChargeOutcome(OffSessionChargeStatus.Succeeded, "pi_x", null, null)
        );
        var customers = new FakeProviderCustomers(null);
        var preferences = new FakeBillingPreferences(AutomaticWithMandate());
        var invoked = new InvokedLegacyBus();

        await SubscriptionRenewalDueConsumer.Handle(
            BuildEvent(),
            invoked,
            preferences,
            customers,
            new FakeFactory(charger),
            new FakeSaaSPaymentRepository(),
            TestPlatform.Settings,
            new FakePaymentAppMetrics(),
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.False(charger.Called);
        Assert.True(invoked.ChargeInvoked);
    }

    [Fact]
    public async Task Requires_user_action_marks_payment_failed_and_publishes_failed_for_dunning()
    {
        var charger = new FakeOffSessionCharger(
            new OffSessionChargeOutcome(
                OffSessionChargeStatus.RequiresUserAction,
                ProviderPaymentReference: null,
                FailureCode: "authentication_required",
                FailureMessage: "SCA"
            )
        );
        var preferences = new FakeBillingPreferences(AutomaticWithMandate());
        var customers = new FakeProviderCustomers(WithDefaultMethod());
        var payments = new FakeSaaSPaymentRepository();
        var bus = new CapturingMessageBus();

        await SubscriptionRenewalDueConsumer.Handle(
            BuildEvent(),
            bus,
            preferences,
            customers,
            new FakeFactory(charger),
            payments,
            TestPlatform.Settings,
            new FakePaymentAppMetrics(),
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(charger.Called);
        Assert.NotNull(payments.Added);
        Assert.Equal(PaymentStatus.Failed, payments.Added!.Status);
        Assert.Contains(bus.Published, m => m is SubscriptionRenewalPaymentFailedIntegrationEvent);
    }

    [Fact]
    public async Task Off_session_hard_failure_publishes_failed_with_retry_hint()
    {
        var charger = new FakeOffSessionCharger(
            new OffSessionChargeOutcome(
                OffSessionChargeStatus.Failed,
                ProviderPaymentReference: null,
                FailureCode: "card_declined",
                FailureMessage: "declined"
            )
        );
        var preferences = new FakeBillingPreferences(AutomaticWithMandate());
        var customers = new FakeProviderCustomers(WithDefaultMethod());
        var payments = new FakeSaaSPaymentRepository();
        var bus = new CapturingMessageBus();

        await SubscriptionRenewalDueConsumer.Handle(
            BuildEvent(),
            bus,
            preferences,
            customers,
            new FakeFactory(charger),
            payments,
            TestPlatform.Settings,
            new FakePaymentAppMetrics(),
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(charger.Called);
        Assert.NotNull(payments.Added);
        Assert.Equal(PaymentStatus.Failed, payments.Added!.Status);
        var failed = Assert.Single(bus.Published.OfType<SubscriptionRenewalPaymentFailedIntegrationEvent>());
        Assert.True(failed.WillRetry);
        Assert.NotNull(failed.NextRetryAtUtc);
    }

    [Fact]
    public async Task Redelivery_with_existing_payment_is_idempotent()
    {
        var payments = new FakeSaaSPaymentRepository { Existing = BuildExistingPayment() };
        var charger = new FakeOffSessionCharger(
            new OffSessionChargeOutcome(OffSessionChargeStatus.Succeeded, "pi_x", null, null)
        );

        await SubscriptionRenewalDueConsumer.Handle(
            BuildEvent(),
            new CapturingMessageBus(),
            new FakeBillingPreferences(AutomaticWithMandate()),
            new FakeProviderCustomers(WithDefaultMethod()),
            new FakeFactory(charger),
            payments,
            TestPlatform.Settings,
            new FakePaymentAppMetrics(),
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.False(charger.Called);
        Assert.Null(payments.Added);
    }

    // --- helpers ---------------------------------------------------------

    private SubscriptionRenewalDueIntegrationEvent BuildEvent() =>
        new()
        {
            TenantId = _tenantId,
            CorrelationId = "corr-1",
            TenantSubscriptionId = _subscriptionId,
            PlanCode = "pro",
            PeriodStartUtc = DateTime.UtcNow,
            PeriodEndUtc = DateTime.UtcNow.AddMonths(1),
            IdempotencyKey = $"sub-renewal-{_subscriptionId:N}",
            AmountCents = 2900,
            Currency = "USD",
        };

    private TenantBillingPreferenceProjection AutomaticWithMandate() =>
        TenantBillingPreferenceProjection.Create(
            _tenantId,
            TenantBillingPreferenceProjection.ModeAutomatic,
            PaymentProviderCode.Stripe.ToString(),
            hasActiveMandate: true,
            agreedAmountCents: 2900,
            agreedCurrency: "USD",
            agreedCadence: "Monthly",
            revisionTicks: 1,
            updatedAtUtc: DateTime.UtcNow
        );

    private TenantProviderCustomer WithDefaultMethod()
    {
        var reference = ProviderCustomerReference.Create(PaymentProviderCode.Stripe, "cus_test_1").Value;
        var customer = TenantProviderCustomer
            .Register(_tenantId, PaymentProviderCode.Stripe, reference, "tenant@example.com", DateTime.UtcNow)
            .Value;
        customer.AttachPaymentMethod(
            methodReference: "pm_test_1",
            brand: "visa",
            last4: "4242",
            expMonth: 12,
            expYear: 2030,
            setAsDefault: true,
            nowUtc: DateTime.UtcNow
        );
        return customer;
    }

    private SaaSPayment BuildExistingPayment()
    {
        var key = IdempotencyKey.Create($"sub-renewal-{_subscriptionId:N}").Value;
        var money = Money.Create(2900, "USD").Value;
        var descriptor = StatementDescriptor.Create("TAXVISION SAAS").Value;
        return SaaSPayment
            .Create(
                _tenantId,
                key,
                money,
                SaaSPaymentType.SubscriptionRenewal,
                _subscriptionId,
                PaymentProviderCode.Stripe,
                descriptor,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
    }

    private sealed class FakeOffSessionCharger(OffSessionChargeOutcome outcome) : IOffSessionCharger
    {
        public bool Called { get; private set; }

        public string ProviderCode => PaymentProviderCode.Stripe.ToString();

        public Task<Result<OffSessionChargeOutcome>> ChargeAsync(OffSessionChargeRequest request, CancellationToken ct)
        {
            Called = true;
            return Task.FromResult(Result.Success(outcome));
        }
    }

    private sealed class FakeFactory(IOffSessionCharger? charger) : IOffSessionChargerFactory
    {
        public IOffSessionCharger? For(string providerCode) =>
            charger is not null && charger.ProviderCode.Equals(providerCode, StringComparison.OrdinalIgnoreCase)
                ? charger
                : null;
    }

    private sealed class FakeBillingPreferences(TenantBillingPreferenceProjection? projection)
        : ITenantBillingPreferenceProjectionRepository
    {
        public Task<TenantBillingPreferenceProjection?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(projection);

        public Task AddAsync(TenantBillingPreferenceProjection added, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeProviderCustomers(TenantProviderCustomer? customer) : ITenantProviderCustomerRepository
    {
        public Task<TenantProviderCustomer?> GetByTenantAndProviderAsync(
            Guid tenantId,
            PaymentProviderCode code,
            CancellationToken ct = default
        ) => Task.FromResult(customer);

        public Task<TenantProviderCustomer?> GetByIdAsync(
            Guid tenantProviderCustomerId,
            Guid tenantId,
            CancellationToken ct = default
        ) => Task.FromResult<TenantProviderCustomer?>(null);

        public Task<IReadOnlyList<TenantProviderCustomer>> GetWithMethodsExpiringBeforeAsync(
            DateTime cutoffUtc,
            int batchSize,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<TenantProviderCustomer>>([]);

        public Task AddAsync(TenantProviderCustomer added, CancellationToken ct = default) => Task.CompletedTask;
    }

    // Variante del CapturingMessageBus que afirma InvokeAsync como "legacy path invocado".
    private sealed class InvokedLegacyBus : Wolverine.IMessageBus
    {
        public bool ChargeInvoked { get; private set; }

        public ValueTask PublishAsync<T>(T message, Wolverine.DeliveryOptions? options = null) =>
            ValueTask.CompletedTask;

        public Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        )
        {
            ChargeInvoked = true;
            // El consumer legacy espera Result<Guid> — simular éxito.
            if (typeof(T) == typeof(BuildingBlocks.Results.Result<Guid>))
                return Task.FromResult((T)(object)BuildingBlocks.Results.Result.Success(Guid.NewGuid()));
            throw new NotSupportedException();
        }

        public Task<T> InvokeAsync<T>(
            object message,
            Wolverine.DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => InvokeAsync<T>(message, cancellation, timeout);

        public ValueTask SendAsync<T>(T message, Wolverine.DeliveryOptions? options = null) =>
            throw new NotSupportedException();

        public ValueTask BroadcastToTopicAsync(
            string topicName,
            object message,
            Wolverine.DeliveryOptions? options = null
        ) => throw new NotSupportedException();

        public IReadOnlyList<Wolverine.Envelope> PreviewSubscriptions(object message) =>
            throw new NotSupportedException();

        public IReadOnlyList<Wolverine.Envelope> PreviewSubscriptions(
            object message,
            Wolverine.DeliveryOptions options
        ) => throw new NotSupportedException();

        public Wolverine.IDestinationEndpoint EndpointFor(string endpointName) => throw new NotSupportedException();

        public Wolverine.IDestinationEndpoint EndpointFor(Uri uri) => throw new NotSupportedException();

        public Task InvokeForTenantAsync(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotSupportedException();

        public Task<T> InvokeForTenantAsync<T>(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotSupportedException();

        public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) =>
            throw new NotSupportedException();

        public Task InvokeAsync(
            object message,
            Wolverine.DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
            object message,
            CancellationToken cancellation = default
        ) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
            object message,
            Wolverine.DeliveryOptions options,
            CancellationToken cancellation = default
        ) => throw new NotSupportedException();

        public string? TenantId { get; set; }
    }
}
