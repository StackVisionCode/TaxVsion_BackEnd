using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Subscription.Application.AddOns.IntegrationEvents;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
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

    /// <summary>
    /// A6/A5.7 — antes esto afirmaba <c>PastDue</c>, y era el sintoma de un callejon sin salida: nadie
    /// movia un add-on de PastDue a GracePeriod, asi que <c>GracePeriodExpirationJob</c> nunca lo
    /// encontraba y el add-on se quedaba en PastDue para siempre. Como PastDue CONSERVA los
    /// entitlements (es gracia), su modulo quedaba habilitado sin volver a pagarse.
    /// </summary>
    [Fact]
    public async Task PaymentFailed_without_retry_opens_the_grace_window_so_the_ladder_can_continue()
    {
        var (addOn, key, _) = ActiveAddOnWithScheduledRenewal();
        var unitOfWork = new FakeUnitOfWork();
        var before = DateTime.UtcNow;

        await Fail(addOn, key, unitOfWork, willRetry: false);

        Assert.Equal(AddOnStatus.GracePeriod, addOn.Status);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);

        // La ventana es la MISMA que la de la suscripcion base: dos plazos distintos para el mismo
        // impago no se le pueden explicar a un tenant.
        Assert.NotNull(addOn.GracePeriodEndsAtUtc);
        var expected = before.AddDays(new SubscriptionOptions().GracePeriodDays);
        Assert.True(
            Math.Abs((addOn.GracePeriodEndsAtUtc!.Value - expected).TotalMinutes) < 5,
            $"La gracia del add-on ({addOn.GracePeriodEndsAtUtc}) no coincide con la politica base ({expected})."
        );
    }

    [Fact]
    public async Task PaymentFailed_with_a_retry_pending_does_not_transition()
    {
        // El reintento todavia puede cobrar: abrir la gracia aqui adelantaria el reloj del impago.
        var (addOn, key, _) = ActiveAddOnWithScheduledRenewal();
        var unitOfWork = new FakeUnitOfWork();

        await Fail(addOn, key, unitOfWork, willRetry: true);

        Assert.Equal(AddOnStatus.Active, addOn.Status);
        Assert.Null(addOn.GracePeriodEndsAtUtc);
    }

    private static Task Fail(TenantAddOn addOn, string key, FakeUnitOfWork unitOfWork, bool willRetry) =>
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
            },
            new FakeTenantAddOnRepo([addOn]),
            unitOfWork,
            new FakeCorrelationContext(),
            Options.Create(new SubscriptionOptions()),
            NullLogger<TenantAddOn>.Instance,
            CancellationToken.None
        );

    private static (TenantAddOn AddOn, string Key, DateTime NewEnd) ActiveAddOnWithScheduledRenewal()
    {
        var nowUtc = DateTime.UtcNow;
        var definition = AddOnDefinition
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
