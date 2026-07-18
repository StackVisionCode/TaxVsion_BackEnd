using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Renewals;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

/// <summary>Confirma que renovar un seat o un add-on no depende de, ni afecta, la
/// suscripción base — cada aggregate gestiona su propio ciclo de renovación.</summary>
public sealed class SeatAndAddOnRenewalTests
{
    [Fact]
    public void Seat_BeginRenewal_is_idempotent_by_key()
    {
        var seat = CreateActiveSeat();

        seat.BeginRenewal("seat-key-1", Guid.Empty, DateTime.UtcNow);
        var result = seat.BeginRenewal("seat-key-1", Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Single(seat.Renewals);
    }

    [Fact]
    public void Seat_CompleteRenewal_advances_only_the_seat_period()
    {
        var seat = CreateActiveSeat();
        var originalEnd = seat.CurrentPeriodEndUtc;
        seat.BeginRenewal("seat-key-1", Guid.Empty, DateTime.UtcNow);
        var renewalId = seat.Renewals.First().Id;

        var result = seat.CompleteRenewal(renewalId, "ext-ref", Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.True(seat.CurrentPeriodEndUtc > originalEnd);
        Assert.Equal(RenewalStatus.Succeeded, seat.Renewals.First().Status);
    }

    [Fact]
    public void AddOn_BeginRenewal_is_idempotent_by_key()
    {
        var addOn = CreateActiveAddOn();

        addOn.BeginRenewal("addon-key-1", Guid.Empty, DateTime.UtcNow);
        var result = addOn.BeginRenewal("addon-key-1", Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Single(addOn.Renewals);
    }

    [Fact]
    public void AddOn_FailRenewal_without_retry_moves_the_addon_to_past_due_without_touching_seats_or_subscription()
    {
        var addOn = CreateActiveAddOn();
        addOn.BeginRenewal("addon-key-1", Guid.Empty, DateTime.UtcNow);
        var renewalId = addOn.Renewals.First().Id;

        var result = addOn.FailRenewal(
            renewalId,
            "card_declined",
            "Card declined",
            willRetry: false,
            null,
            Guid.Empty,
            DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(AddOnStatus.PastDue, addOn.Status);
    }

    private static SubscriptionSeat CreateActiveSeat()
    {
        var seat = SubscriptionSeat
            .Purchase(
                Guid.NewGuid(),
                SeatType.Standard,
                SeatSourceType.Plan,
                null,
                Money.Zero("USD"),
                BillingCycle.Monthly,
                true,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
        var nowUtc = DateTime.UtcNow;
        seat.Activate(nowUtc, nowUtc.AddMonths(1), Guid.Empty, nowUtc);
        return seat;
    }

    private static TenantAddOn CreateActiveAddOn()
    {
        var definition = AddOnDefinition
            .Create(
                AddOnCode.Create("storage.extra_100gb").Value,
                "Extra storage",
                "desc",
                "storage",
                true,
                [BillingCycle.Monthly],
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
        return TenantAddOn
            .Purchase(
                Guid.NewGuid(),
                definition,
                1,
                Money.Zero("USD"),
                BillingCycle.Monthly,
                true,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
    }
}
