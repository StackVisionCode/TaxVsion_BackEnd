using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ChargeSaaSPayment;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ExecuteSaaSPaymentCharge;
using TaxVision.PaymentApp.Application.SaaSPayments.IntegrationEvents;
using TaxVision.PaymentApp.Domain.Billing;
using TaxVision.PaymentApp.Domain.ProviderCustomers;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>Caracterización de PAY-7: cada rama de la selección de estrategia y el comando que deja.</summary>
public sealed partial class SubscriptionRenewalDueConsumerTests
{
    private static readonly OffSessionChargeOutcome AnyOutcome = new(
        OffSessionChargeStatus.Succeeded,
        "pi_x",
        null,
        null
    );

    [Fact]
    public async Task No_projected_preference_goes_to_the_legacy_charge_with_Stripe()
    {
        var legacy = await RunLegacyAsync(null, WithDefaultMethod());

        Assert.Equal(PaymentProviderCode.Stripe, legacy.Provider);
    }

    [Fact]
    public async Task Unknown_provider_code_goes_to_the_legacy_charge_with_Stripe()
    {
        var legacy = await RunLegacyAsync(Automatic("Bitcoin"), WithDefaultMethod());

        Assert.Equal(PaymentProviderCode.Stripe, legacy.Provider);
    }

    [Fact]
    public async Task Provider_without_charger_goes_to_the_legacy_charge_with_that_provider()
    {
        var legacy = await RunLegacyAsync(Automatic("paypal"), WithDefaultMethod());

        Assert.Equal(PaymentProviderCode.PayPal, legacy.Provider);
    }

    [Fact]
    public async Task Customer_without_a_default_method_goes_to_the_legacy_charge()
    {
        var reference = ProviderCustomerReference.Create(PaymentProviderCode.Stripe, "cus_empty").Value;
        var customer = TenantProviderCustomer
            .Register(_tenantId, PaymentProviderCode.Stripe, reference, "tenant@example.com", DateTime.UtcNow)
            .Value;

        var legacy = await RunLegacyAsync(AutomaticWithMandate(), customer);

        Assert.Equal(PaymentProviderCode.Stripe, legacy.Provider);
    }

    [Fact]
    public async Task Legacy_charge_carries_the_renewal_as_it_came()
    {
        var reservation = Guid.NewGuid();
        var reservationPayment = Guid.NewGuid();
        var renewal = BuildRenewal(
            $"sub-renewal-{_subscriptionId:N}",
            reservation,
            reservationPayment,
            discountCents: 100,
            promotionHash: "hash-1"
        );
        var bus = new InvokedLegacyBus();

        await RunAsync(renewal, bus, null, WithDefaultMethod(), new FakeSaaSPaymentRepository());

        var legacy = Assert.IsType<ChargeSaaSPaymentCommand>(bus.Invoked);
        Assert.Equal(_tenantId, legacy.TenantId);
        Assert.Equal(renewal.IdempotencyKey, legacy.IdempotencyKey);
        Assert.Equal(2900, legacy.AmountCents);
        Assert.Equal("USD", legacy.Currency);
        Assert.Equal(SaaSPaymentType.SubscriptionRenewal, legacy.Type);
        Assert.Equal(_subscriptionId, legacy.TargetAggregateId);
        Assert.Equal(Guid.Empty, legacy.RequestedByUserId);
        Assert.Equal(reservation, legacy.CodeReservationId);
        Assert.Equal(reservationPayment, legacy.CodeReservationPaymentId);
        Assert.Equal(100, legacy.DiscountAmountCents);
        Assert.Equal("hash-1", legacy.PromotionSnapshotHash);
    }

    [Fact]
    public async Task Invalid_renewal_for_off_session_goes_to_the_legacy_charge_without_a_payment()
    {
        var renewal = BuildRenewal(string.Empty, null, null, null, null);
        var bus = new InvokedLegacyBus();
        var payments = new FakeSaaSPaymentRepository();

        await RunAsync(renewal, bus, AutomaticWithMandate(), WithDefaultMethod(), payments);

        Assert.Null(payments.Added);
        Assert.Equal(PaymentProviderCode.Stripe, Assert.IsType<ChargeSaaSPaymentCommand>(bus.Invoked).Provider);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Off_session_payment_and_command_carry_the_renewal_and_the_default_method()
    {
        var reservation = Guid.NewGuid();
        var reservationPayment = Guid.NewGuid();
        var renewal = BuildRenewal(
            $"sub-renewal-{_subscriptionId:N}",
            reservation,
            reservationPayment,
            discountCents: 100,
            promotionHash: "hash-1"
        );
        var bus = new InvokedLegacyBus();
        var payments = new FakeSaaSPaymentRepository();

        await RunAsync(renewal, bus, AutomaticWithMandate(), WithDefaultMethod(), payments);

        Assert.False(bus.ChargeInvoked);
        var payment = payments.Added!;
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(2900, payment.Amount.AmountCents);
        Assert.Equal("USD", payment.Amount.Currency);
        Assert.Equal(SaaSPaymentType.SubscriptionRenewal, payment.Type);
        Assert.Equal(_subscriptionId, payment.TargetAggregateId);
        Assert.Equal(PaymentProviderCode.Stripe, payment.ProviderCode);
        Assert.Equal(reservation, payment.CodeReservationId);
        Assert.Equal(reservationPayment, payment.CodeReservationPaymentId);
        Assert.Equal(100, payment.DiscountAmountCents);
        Assert.Equal("hash-1", payment.PromotionSnapshotHash);
        Assert.NotNull(payment.Concept);

        var command = Assert.IsType<ExecuteSaaSPaymentChargeCommand>(Assert.Single(bus.Published));
        Assert.Equal(_tenantId, command.TenantId);
        Assert.Equal(payment.Id, command.SaaSPaymentId);
        Assert.Equal(SaaSPaymentChargeStrategy.OffSession, command.Strategy);
        Assert.Equal("pm_test_1", command.MethodReference);
        Assert.Null(command.CustomerReference);
        Assert.Equal(Guid.Empty, command.ActorUserId);
        Assert.Equal("corr-1", command.CorrelationId);
    }

    [Fact]
    public async Task Renewal_without_a_correlation_id_uses_its_event_id()
    {
        var renewal = BuildRenewal($"sub-renewal-{_subscriptionId:N}", null, null, null, null, correlationId: " ");
        var bus = new InvokedLegacyBus();

        await RunAsync(renewal, bus, AutomaticWithMandate(), WithDefaultMethod(), new FakeSaaSPaymentRepository());

        var command = Assert.IsType<ExecuteSaaSPaymentChargeCommand>(Assert.Single(bus.Published));
        Assert.Equal(renewal.EventId.ToString("N"), command.CorrelationId);
    }

    private TenantBillingPreferenceProjection Automatic(string providerCode) =>
        TenantBillingPreferenceProjection.Create(
            _tenantId,
            TenantBillingPreferenceProjection.ModeAutomatic,
            providerCode,
            hasActiveMandate: true,
            agreedAmountCents: 2900,
            agreedCurrency: "USD",
            agreedCadence: "Monthly",
            revisionTicks: 1,
            updatedAtUtc: DateTime.UtcNow
        );

    private SubscriptionRenewalDueIntegrationEvent BuildRenewal(
        string idempotencyKey,
        Guid? reservationId,
        Guid? reservationPaymentId,
        long? discountCents,
        string? promotionHash,
        string correlationId = "corr-1"
    ) =>
        new()
        {
            TenantId = _tenantId,
            CorrelationId = correlationId,
            TenantSubscriptionId = _subscriptionId,
            PlanCode = "pro",
            PeriodStartUtc = DateTime.UtcNow,
            PeriodEndUtc = DateTime.UtcNow.AddMonths(1),
            IdempotencyKey = idempotencyKey,
            AmountCents = 2900,
            Currency = "USD",
            CodeReservationId = reservationId,
            CodeReservationPaymentId = reservationPaymentId,
            DiscountAmountCents = discountCents,
            PromotionSnapshotHash = promotionHash,
        };

    private async Task<ChargeSaaSPaymentCommand> RunLegacyAsync(
        TenantBillingPreferenceProjection? preference,
        TenantProviderCustomer customer
    )
    {
        var bus = new InvokedLegacyBus();
        var payments = new FakeSaaSPaymentRepository();

        await RunAsync(BuildEvent(), bus, preference, customer, payments);

        Assert.Null(payments.Added);
        return Assert.IsType<ChargeSaaSPaymentCommand>(bus.Invoked);
    }

    private static Task RunAsync(
        SubscriptionRenewalDueIntegrationEvent renewal,
        InvokedLegacyBus bus,
        TenantBillingPreferenceProjection? preference,
        TenantProviderCustomer customer,
        FakeSaaSPaymentRepository payments
    ) =>
        SubscriptionRenewalDueConsumer.Handle(
            renewal,
            bus,
            new FakeBillingPreferences(preference),
            new FakeProviderCustomers(customer),
            new FakeFactory(new FakeOffSessionCharger(AnyOutcome)),
            payments,
            TestPlatform.Settings,
            new FakePaymentAppMetrics(),
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );
}
