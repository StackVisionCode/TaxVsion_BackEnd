using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

public sealed class SubscriptionSeatTests
{
    [Fact]
    public void Purchase_starts_in_available_status()
    {
        var seat = CreateSeat();

        Assert.Equal(SeatStatus.Available, seat.Status);
        Assert.Null(seat.CurrentPeriodEndUtc);
    }

    [Fact]
    public void Activate_sets_the_period_and_next_renewal()
    {
        var seat = CreateSeat();
        var start = DateTime.UtcNow;
        var end = start.AddMonths(1);

        var result = seat.Activate(start, end, Guid.Empty, start);

        Assert.True(result.IsSuccess);
        Assert.Equal(SeatStatus.Active, seat.Status);
        Assert.Equal(end, seat.NextRenewalAtUtc);
    }

    [Fact]
    public void Cannot_activate_a_seat_that_is_already_active()
    {
        var seat = CreateSeat();
        var now = DateTime.UtcNow;
        seat.Activate(now, now.AddMonths(1), Guid.Empty, now);

        var result = seat.Activate(now, now.AddMonths(1), Guid.Empty, now);

        Assert.True(result.IsFailure);
        Assert.Equal("Seat.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void SuspendForPolicyViolation_from_available_is_allowed()
    {
        var seat = CreateSeat();

        var result = seat.SuspendForPolicyViolation("abuse", Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(SeatStatus.Suspended, seat.Status);
    }

    [Fact]
    public void CancelActive_requires_active_pastdue_or_grace_status()
    {
        var seat = CreateSeat();

        var result = seat.CancelActive("no longer needed", Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Seat.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void BeginInitialCharge_schedules_a_charge_over_the_current_period_without_advancing()
    {
        var seat = CreateSeat();
        var start = DateTime.UtcNow;
        var end = start.AddMonths(1);
        seat.Activate(start, end, Guid.Empty, start);

        var result = seat.BeginInitialCharge("seat-initial-1", Guid.Empty, start);

        Assert.True(result.IsSuccess);
        var charge = Assert.Single(seat.Renewals);
        Assert.Equal(start, charge.PeriodStartUtc);
        Assert.Equal(end, charge.PeriodEndUtc);
        // El período del asiento no se movió: sigue siendo el vigente (el cargo inicial no avanza).
        Assert.Equal(start, seat.CurrentPeriodStartUtc);
        Assert.Equal(end, seat.CurrentPeriodEndUtc);
    }

    [Fact]
    public void BeginInitialCharge_is_idempotent_by_key()
    {
        var seat = CreateSeat();
        var now = DateTime.UtcNow;
        seat.Activate(now, now.AddMonths(1), Guid.Empty, now);

        seat.BeginInitialCharge("seat-initial-1", Guid.Empty, now);
        var second = seat.BeginInitialCharge("seat-initial-1", Guid.Empty, now);

        Assert.True(second.IsSuccess);
        Assert.Single(seat.Renewals);
    }

    [Fact]
    public void BeginInitialCharge_requires_an_active_seat()
    {
        var seat = CreateSeat(); // Available

        var result = seat.BeginInitialCharge("seat-initial-1", Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Seat.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void CompleteRenewal_after_an_initial_charge_keeps_the_same_period()
    {
        // Reusar el pipeline de renovación para el cargo inicial no debe avanzar el período:
        // CompleteRenewal fija el período al del intento, que es el vigente.
        var seat = CreateSeat();
        var start = DateTime.UtcNow;
        var end = start.AddMonths(1);
        seat.Activate(start, end, Guid.Empty, start);
        seat.BeginInitialCharge("seat-initial-1", Guid.Empty, start);
        var chargeId = seat.Renewals.Single().Id;

        var result = seat.CompleteRenewal(chargeId, "pi_123", Guid.Empty, start);

        Assert.True(result.IsSuccess);
        Assert.Equal(SeatStatus.Active, seat.Status);
        Assert.Equal(start, seat.CurrentPeriodStartUtc);
        Assert.Equal(end, seat.CurrentPeriodEndUtc);
    }

    private static SubscriptionSeat CreateSeat() =>
        SubscriptionSeat
            .Purchase(
                Guid.NewGuid(),
                SeatType.Standard,
                SeatSourceType.Plan,
                sourceReferenceId: null,
                Money.Create(9m, "USD").Value,
                BillingCycle.Monthly,
                autoRenew: true,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
}
