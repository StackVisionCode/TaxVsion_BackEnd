using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

/// <summary>Las reglas domésticas que ejecuta el sweeper en cada iteración: Expire respeta sesiones abiertas,
/// las vencidas pasan a Expired, y no toca intenciones Paid.</summary>
public sealed class CheckoutIntentExpireSweeperDomainTests
{
    [Fact]
    public void Sweeper_only_touches_expired_pending_intents()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var expired = SeatPurchaseIntent
            .Create(
                tenantId,
                SeatType.Standard,
                1,
                false,
                Money.Create(10m, "USD").Value,
                BillingCycle.Monthly,
                1000,
                Guid.NewGuid(),
                now.AddHours(-2)
            )
            .Value;
        expired.AttachCheckout(Guid.NewGuid(), "https://x", now.AddHours(-1), now.AddHours(-2));

        var stillOpen = SeatPurchaseIntent
            .Create(
                tenantId,
                SeatType.Standard,
                1,
                false,
                Money.Create(10m, "USD").Value,
                BillingCycle.Monthly,
                1000,
                Guid.NewGuid(),
                now
            )
            .Value;
        stillOpen.AttachCheckout(Guid.NewGuid(), "https://x", now.AddHours(1), now);

        var paid = SeatPurchaseIntent
            .Create(
                tenantId,
                SeatType.Standard,
                1,
                false,
                Money.Create(10m, "USD").Value,
                BillingCycle.Monthly,
                1000,
                Guid.NewGuid(),
                now
            )
            .Value;
        paid.AttachCheckout(Guid.NewGuid(), "https://x", now.AddHours(-1), now);
        paid.MarkPaid(now);

        expired.Expire(now);
        stillOpen.Expire(now);
        paid.Expire(now);

        Assert.Equal(SeatPurchaseIntentStatus.Expired, expired.Status);
        Assert.Equal(SeatPurchaseIntentStatus.Pending, stillOpen.Status);
        Assert.Equal(SeatPurchaseIntentStatus.Paid, paid.Status);
    }

    [Fact]
    public void Sweeper_leaves_awaiting_payment_untouched_if_still_open_for_upgrade()
    {
        var request = PlanChangeRequest
            .Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "starter",
                Guid.NewGuid(),
                Guid.NewGuid(),
                "pro",
                BillingCycle.Monthly,
                Guid.NewGuid(),
                DateTime.UtcNow,
                4900,
                "USD",
                "idemp"
            )
            .Value;
        request.AttachCheckout(Guid.NewGuid(), "https://x", DateTime.UtcNow.AddHours(1));

        request.Expire(DateTime.UtcNow);

        Assert.Equal(PlanChangeRequestStatus.AwaitingPayment, request.Status);
    }
}
