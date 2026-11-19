using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.WalletIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ChargeSaaSPayment;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ExecuteSaaSPaymentCharge;
using TaxVision.PaymentApp.Application.SaaSPayments.IntegrationEvents;
using TaxVision.PaymentApp.Domain.ProviderCustomers;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>
/// Recarga del monedero. La automática cobra off-session solo el método que la oficina autorizó en Wallet (sin
/// mandato de la suscripción); si ese método ya no es suyo o no está activo, falla con <c>MethodUnavailable</c> sin
/// llamar al proveedor.
/// </summary>
public sealed class WalletTopUpDueConsumerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _topUpId = Guid.NewGuid();

    [Fact]
    public async Task Automatic_with_valid_stripe_method_charges_that_method_off_session()
    {
        var harness = new Harness();
        var customer = Customer(_tenantId, PaymentProviderCode.Stripe, "pm_default", "pm_authorized");
        harness.Customers.Add(customer);

        var bus = await harness.DeliverAsync(AutomaticEvent("Stripe", IdOf(customer, "pm_authorized")));

        var command = Assert.Single(bus.Published.OfType<ExecuteSaaSPaymentChargeCommand>());
        Assert.Equal(SaaSPaymentChargeStrategy.OffSession, command.Strategy);
        var request = Assert.Single(harness.Stripe.Requests);
        Assert.Equal("pm_authorized", request.ProviderToken);
        Assert.Empty(harness.PayPal.Requests);
        Assert.Equal(PaymentStatus.Succeeded, harness.Payments.Added!.Status);
        var emitted = await SaaSPaymentEventReplay.EmittedAsync(bus.Published, harness.Payments.Added);
        Assert.Single(emitted.OfType<WalletTopUpPaymentSucceededIntegrationEvent>());
    }

    [Fact]
    public async Task Automatic_with_paypal_method_uses_the_paypal_charger()
    {
        var harness = new Harness();
        var customer = PayPalCustomer(_tenantId, "vault_token_1");
        harness.Customers.Add(customer);

        await harness.DeliverAsync(AutomaticEvent("PayPal", IdOf(customer, "vault_token_1")));

        var request = Assert.Single(harness.PayPal.Requests);
        Assert.Equal("vault_token_1", request.ProviderToken);
        Assert.Empty(harness.Stripe.Requests);
        Assert.Equal(PaymentProviderCode.PayPal, harness.Payments.Added!.ProviderCode);
        Assert.Equal(PaymentStatus.Succeeded, harness.Payments.Added.Status);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("other-tenant")]
    [InlineData("detached")]
    [InlineData("expired")]
    [InlineData("no-method")]
    [InlineData("provider-reference")]
    public async Task Automatic_with_unavailable_method_fails_without_calling_the_provider(string scenario)
    {
        var harness = new Harness();
        var (customer, reference) = UnavailableScenario(scenario);
        harness.Customers.Add(customer);

        var bus = await harness.DeliverAsync(AutomaticEvent("Stripe", reference));

        Assert.Empty(bus.Published.OfType<ExecuteSaaSPaymentChargeCommand>());
        Assert.Empty(harness.Stripe.Requests);
        Assert.Empty(harness.PayPal.Requests);
        Assert.Equal(PaymentStatus.Failed, harness.Payments.Added!.Status);
        Assert.Equal("MethodUnavailable", harness.Payments.Added.FailureCode);
        var emitted = await SaaSPaymentEventReplay.DispatchEventsAsync(harness.EventsBeforeDrain);
        var failed = Assert.Single(emitted.OfType<WalletTopUpPaymentFailedIntegrationEvent>());
        Assert.Equal("MethodUnavailable", failed.Reason);
        Assert.Equal(_topUpId, failed.TopUpId);
    }

    [Fact]
    public async Task Redelivery_of_the_same_event_charges_once()
    {
        var harness = new Harness();
        var customer = Customer(_tenantId, PaymentProviderCode.Stripe, "pm_authorized");
        harness.Customers.Add(customer);
        var evt = AutomaticEvent("Stripe", IdOf(customer, "pm_authorized"));

        await harness.DeliverAsync(evt);
        harness.Payments.Existing = harness.Payments.Added;
        var second = await harness.DeliverAsync(evt);

        Assert.Single(harness.Stripe.Requests);
        Assert.Empty(second.Published.OfType<ExecuteSaaSPaymentChargeCommand>());
    }

    [Fact]
    public async Task Manual_top_up_respects_the_event_provider()
    {
        var harness = new Harness();
        harness.Customers.Add(PayPalCustomer(_tenantId, "vault_token_1"));
        var evt = AutomaticEvent("PayPal", null) with { Automatic = false };

        var bus = await harness.DeliverAsync(evt, drain: false);

        var command = Assert.Single(bus.Published.OfType<ExecuteSaaSPaymentChargeCommand>());
        Assert.Equal(SaaSPaymentChargeStrategy.Authorize, command.Strategy);
        Assert.Equal("vault_token_1", command.MethodReference);
        Assert.Equal(PaymentProviderCode.PayPal, harness.Payments.Added!.ProviderCode);
    }

    // --- helpers ---------------------------------------------------------

    private WalletTopUpDueIntegrationEvent AutomaticEvent(string? provider, string? methodReference) =>
        new()
        {
            TenantId = _tenantId,
            CorrelationId = "corr-wallet",
            TopUpId = _topUpId,
            AmountCents = 2_500,
            Currency = "USD",
            IdempotencyKey = $"wallet-topup-{_topUpId:N}",
            RequestedByUserId = Guid.Empty,
            Automatic = true,
            Provider = provider,
            PaymentMethodReference = methodReference,
        };

    // El evento lleva el Id interno del método (el que ve el CRM), nunca la referencia del proveedor.
    private (TenantProviderCustomer Customer, string? MethodId) UnavailableScenario(string scenario)
    {
        switch (scenario)
        {
            case "other-tenant":
                var foreign = Customer(Guid.NewGuid(), PaymentProviderCode.Stripe, "pm_authorized");
                return (foreign, IdOf(foreign, "pm_authorized"));
            case "detached":
                var customer = Customer(_tenantId, PaymentProviderCode.Stripe, "pm_default", "pm_authorized");
                var authorized = customer.SavedMethods.Single(m => m.MethodReference == "pm_authorized");
                Assert.True(customer.DetachPaymentMethod(authorized.Id, DateTime.UtcNow).IsSuccess);
                return (customer, authorized.Id.ToString());
            case "expired":
                var expired = Customer(_tenantId, PaymentProviderCode.Stripe, "pm_default");
                expired.AttachPaymentMethod("pm_authorized", "visa", "4242", 1, 2020, false, DateTime.UtcNow);
                return (expired, IdOf(expired, "pm_authorized"));
            case "no-method":
                return (Customer(_tenantId, PaymentProviderCode.Stripe, "pm_default"), null);
            case "provider-reference":
                return (Customer(_tenantId, PaymentProviderCode.Stripe, "pm_authorized"), "pm_authorized");
            default:
                // Id que no existe: nunca se cae al default.
                return (Customer(_tenantId, PaymentProviderCode.Stripe, "pm_default"), Guid.NewGuid().ToString());
        }
    }

    private static string IdOf(TenantProviderCustomer customer, string providerReference) =>
        customer.SavedMethods.Single(m => m.MethodReference == providerReference).Id.ToString();

    private static TenantProviderCustomer Customer(
        Guid tenantId,
        PaymentProviderCode code,
        params string[] methodReferences
    )
    {
        var reference = ProviderCustomerReference.Create(code, "cus_wallet_1").Value;
        var customer = TenantProviderCustomer
            .Register(tenantId, code, reference, "tenant@example.com", DateTime.UtcNow)
            .Value;
        foreach (var method in methodReferences)
            customer.AttachPaymentMethod(method, "visa", "4242", 12, 2099, false, DateTime.UtcNow);
        return customer;
    }

    // Las cuentas de PayPal no tienen vencimiento: se guardan con mes y año 0.
    private static TenantProviderCustomer PayPalCustomer(Guid tenantId, string vaultToken)
    {
        var reference = ProviderCustomerReference.Create(PaymentProviderCode.PayPal, "payer_1").Value;
        var customer = TenantProviderCustomer
            .Register(tenantId, PaymentProviderCode.PayPal, reference, "tenant@example.com", DateTime.UtcNow)
            .Value;
        customer.AttachPaymentMethod(vaultToken, "PayPal", "", 0, 0, true, DateTime.UtcNow);
        return customer;
    }

    private sealed class Harness
    {
        public FakeSaaSPaymentRepository Payments { get; } = new();
        public ProviderCustomers Customers { get; } = new();
        public RecordingCharger Stripe { get; } = new(PaymentProviderCode.Stripe);
        public RecordingCharger PayPal { get; } = new(PaymentProviderCode.PayPal);

        // Drenar siembra el pago como guardado y limpia sus eventos: se guardan antes.
        public List<BuildingBlocks.Domain.IDomainEvent> EventsBeforeDrain { get; } = [];

        public async Task<CapturingMessageBus> DeliverAsync(WalletTopUpDueIntegrationEvent evt, bool drain = true)
        {
            var bus = new CapturingMessageBus();
            bus.Route = message =>
                message is ChargeSaaSPaymentCommand charge
                    ? ChargeSaaSPaymentHandler.Handle(
                        charge,
                        Payments,
                        Customers,
                        new AnyProviderAdapters(),
                        new FakePaymentAuditLogWriter(),
                        new FakeUnitOfWork(),
                        new FakePaymentAppMetrics(),
                        new FakeCorrelationContext(),
                        NullLogger<SaaSPayment>.Instance,
                        TestPlatform.Settings,
                        bus,
                        CancellationToken.None
                    )
                    : throw new NotSupportedException(message.GetType().Name);

            await WalletTopUpDueConsumer.Handle(
                evt,
                bus,
                new FakeCorrelationContext(),
                NullLogger<SaaSPayment>.Instance,
                CancellationToken.None
            );

            EventsBeforeDrain.AddRange(Payments.Added?.DomainEvents ?? []);
            if (drain)
                await TestChargeExecution.DrainAsync(
                    bus,
                    Payments,
                    new Chargers(Stripe, PayPal),
                    new AnyProviderAdapters()
                );
            return bus;
        }
    }

    private sealed class RecordingCharger(PaymentProviderCode code) : IOffSessionCharger
    {
        public List<OffSessionChargeRequest> Requests { get; } = [];

        public string ProviderCode => code.ToString();

        public Task<Result<OffSessionChargeOutcome>> ChargeAsync(OffSessionChargeRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(
                Result.Success(
                    new OffSessionChargeOutcome(OffSessionChargeStatus.Succeeded, $"ref_{Requests.Count}", null, null)
                )
            );
        }
    }

    private sealed class Chargers(params IOffSessionCharger[] chargers) : IOffSessionChargerFactory
    {
        public IOffSessionCharger? For(string providerCode) =>
            chargers.FirstOrDefault(c => c.ProviderCode.Equals(providerCode, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class AnyProviderAdapters : IPaymentAdapterFactory
    {
        public IPaymentProvider Resolve(PaymentProviderCode code) => new FakePaymentProvider(code);
    }

    // Igual que una fuga de tenant: devuelve el customer del proveedor sin mirar el tenant pedido.
    private sealed class ProviderCustomers : ITenantProviderCustomerRepository
    {
        private readonly List<TenantProviderCustomer> _customers = [];

        public void Add(TenantProviderCustomer customer) => _customers.Add(customer);

        public Task<TenantProviderCustomer?> GetByTenantAndProviderAsync(
            Guid tenantId,
            PaymentProviderCode code,
            CancellationToken ct = default
        ) => Task.FromResult(_customers.FirstOrDefault(c => c.ProviderCode == code));

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
}
