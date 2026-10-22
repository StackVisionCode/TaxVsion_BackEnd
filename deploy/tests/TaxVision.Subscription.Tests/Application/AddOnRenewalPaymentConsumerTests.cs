using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.AddOns.IntegrationEvents;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Renewals;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>Cierra el loop de cobro del add-on: el intent (AddOnRenewalDue) → PaymentApp cobra →
/// PaymentSucceeded/Failed → estos consumers aplican CompleteRenewal / FailRenewal por IdempotencyKey.</summary>
public sealed class AddOnRenewalPaymentConsumerTests
{
    [Fact]
    public async Task PaymentSucceeded_advances_the_addon_period_for_the_matching_renewal()
    {
        var (addOn, key, newEnd) = ActiveAddOnWithScheduledRenewal();
        var unitOfWork = new FakeUnitOfWork();

        await AddOnRenewalPaymentSucceededConsumer.Handle(
            new AddOnRenewalPaymentSucceededIntegrationEvent
            {
                TenantId = addOn.TenantId,
                TenantAddOnId = addOn.Id,
                SaaSPaymentId = Guid.NewGuid(),
                IdempotencyKey = key,
                ExternalPaymentReference = "ext-ref",
                PaidAtUtc = DateTime.UtcNow,
            },
            new FakeTenantAddOnRepo([addOn]),
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<TenantAddOn>.Instance,
            CancellationToken.None
        );

        Assert.Equal(AddOnStatus.Active, addOn.Status);
        Assert.Equal(newEnd, addOn.CurrentPeriodEndUtc);
        Assert.Equal(RenewalStatus.Succeeded, addOn.Renewals.First().Status);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    /// <summary>Al agotarse los reintentos, el add-on se suspende de una: ya no se da ventana de gracia
    /// con acceso vivo.</summary>
    [Fact]
    public async Task PaymentFailed_without_retry_suspends_the_addon()
    {
        var (addOn, key, _) = ActiveAddOnWithScheduledRenewal();
        var unitOfWork = new FakeUnitOfWork();

        await Fail(addOn, key, unitOfWork, willRetry: false);

        Assert.Equal(AddOnStatus.Suspended, addOn.Status);
        Assert.Equal("RenewalFailed", addOn.SuspensionReason);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task PaymentFailed_with_a_retry_pending_does_not_transition()
    {
        // El reintento todavia puede cobrar: abrir la gracia aqui adelantaria el reloj del impago.
        var (addOn, key, _) = ActiveAddOnWithScheduledRenewal();
        var unitOfWork = new FakeUnitOfWork();

        await Fail(addOn, key, unitOfWork, willRetry: true);

        Assert.Equal(AddOnStatus.Active, addOn.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Automatic_failure_asks_for_the_failed_charge_email_with_the_add_on_name(bool willRetry)
    {
        var (addOn, key, _) = ActiveAddOnWithScheduledRenewal();
        var bus = new CapturingMessageBus();

        await Fail(addOn, key, new FakeUnitOfWork(), willRetry, bus, automatic: true);

        var email = Assert.Single(bus.Published.OfType<RenewalChargeFailedIntegrationEvent>());
        Assert.Equal(RenewalChargeKinds.AddOn, email.ChargeKind);
        Assert.Equal("Email add-on", email.ItemDescription);
        Assert.Equal(!willRetry, email.IsFinal);
        Assert.Equal(willRetry ? null : RenewalChargeConsequences.AddOnSuspended, email.Consequence);
    }

    [Fact]
    public async Task A_hosted_checkout_failure_sends_no_failed_charge_email()
    {
        var (addOn, key, _) = ActiveAddOnWithScheduledRenewal();
        var bus = new CapturingMessageBus();

        await Fail(addOn, key, new FakeUnitOfWork(), willRetry: false, bus, automatic: false);

        Assert.Empty(bus.Published.OfType<RenewalChargeFailedIntegrationEvent>());
    }

    private static Task Fail(
        TenantAddOn addOn,
        string key,
        FakeUnitOfWork unitOfWork,
        bool willRetry,
        CapturingMessageBus? bus = null,
        bool automatic = false
    ) =>
        AddOnRenewalPaymentFailedConsumer.Handle(
            new AddOnRenewalPaymentFailedIntegrationEvent
            {
                TenantId = addOn.TenantId,
                TenantAddOnId = addOn.Id,
                SaaSPaymentId = Guid.NewGuid(),
                IdempotencyKey = key,
                FailureCode = "card_declined",
                FailureReason = "Card declined",
                WillRetry = willRetry,
                NextRetryAtUtc = willRetry ? DateTime.UtcNow.AddDays(3) : null,
                AttemptNumber = 1,
                MaxAttempts = 4,
                AmountCents = 2900,
                Currency = "USD",
                IsAutomaticCharge = automatic,
            },
            new FakeTenantAddOnRepo([addOn]),
            new FakeAddOnDefinitionRepository(EmailDefinition(DateTime.UtcNow)),
            unitOfWork,
            bus ?? new CapturingMessageBus(),
            new FakeCorrelationContext(),
            NullLogger<TenantAddOn>.Instance,
            CancellationToken.None
        );

    private static AddOnDefinition EmailDefinition(DateTime nowUtc) =>
        AddOnDefinition
            .Create(
                AddOnCode.Create("email.addon").Value,
                "Email",
                "Email",
                "module",
                false,
                [BillingCycle.Monthly],
                Guid.Empty,
                nowUtc
            )
            .Value;

    private static (TenantAddOn AddOn, string Key, DateTime NewEnd) ActiveAddOnWithScheduledRenewal()
    {
        var nowUtc = DateTime.UtcNow;
        var definition = EmailDefinition(nowUtc);
        var addOn = TenantAddOn
            .Purchase(
                Guid.NewGuid(),
                definition,
                1,
                Money.Create(29m, "USD").Value,
                BillingCycle.Monthly,
                true,
                Guid.Empty,
                nowUtc
            )
            .Value;

        var newEnd = addOn.CurrentPeriodEndUtc.AddMonths(1);
        const string key = "addon-renewal-key-1";
        addOn.BeginRenewal(key, newEnd, Guid.Empty, nowUtc);
        return (addOn, key, newEnd);
    }
}
