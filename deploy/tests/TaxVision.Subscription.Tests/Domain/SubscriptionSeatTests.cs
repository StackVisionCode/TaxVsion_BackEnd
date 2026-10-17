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

    // -------- Reactivación por pago manual del admin (B5) --------

    [Fact]
    public void BeginReactivation_from_suspended_schedules_a_new_period_renewal()
    {
        var seat = SuspendedSeat(out var suspendedAt);
        var now = suspendedAt.AddHours(2);
        var newEnd = now.AddMonths(1);

        var result = seat.BeginReactivation("seat-reactivation-1", newEnd, Guid.Empty, now);

        Assert.True(result.IsSuccess);
        var renewal = Assert.Single(seat.Renewals);
        Assert.Equal(now, renewal.PeriodStartUtc);
        Assert.Equal(newEnd, renewal.PeriodEndUtc);
        // El seat sigue Suspended hasta CompleteReactivation.
        Assert.Equal(SeatStatus.Suspended, seat.Status);
    }

    [Fact]
    public void BeginReactivation_from_pastdue_is_allowed()
    {
        var seat = PastDueSeat();
        var now = DateTime.UtcNow;

        var result = seat.BeginReactivation("seat-reactivation-1", now.AddMonths(1), Guid.Empty, now);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void BeginReactivation_rejects_active_seats()
    {
        var seat = ActiveSeat();
        var now = DateTime.UtcNow;

        var result = seat.BeginReactivation("seat-reactivation-1", now.AddMonths(1), Guid.Empty, now);

        Assert.True(result.IsFailure);
        Assert.Equal("Seat.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void BeginReactivation_is_idempotent_by_key()
    {
        var seat = SuspendedSeat(out var suspendedAt);
        var now = suspendedAt.AddHours(1);
        seat.BeginReactivation("seat-reactivation-1", now.AddMonths(1), Guid.Empty, now);

        var second = seat.BeginReactivation("seat-reactivation-1", now.AddMonths(1), Guid.Empty, now);

        Assert.True(second.IsSuccess);
        Assert.Single(seat.Renewals);
    }

    [Fact]
    public void BeginReactivation_rejects_a_period_end_in_the_past()
    {
        var seat = SuspendedSeat(out var suspendedAt);
        var now = suspendedAt.AddHours(1);

        var result = seat.BeginReactivation("seat-reactivation-1", now.AddMinutes(-5), Guid.Empty, now);

        Assert.True(result.IsFailure);
        Assert.Equal("Seat.InvalidPeriod", result.Error.Code);
    }

    [Fact]
    public void CompleteReactivation_transitions_suspended_to_active_with_new_period()
    {
        var seat = SuspendedSeat(out var suspendedAt);
        var now = suspendedAt.AddHours(2);
        var newEnd = now.AddMonths(1);
        seat.BeginReactivation("seat-reactivation-1", newEnd, Guid.Empty, now);
        var renewalId = seat.Renewals.Single().Id;

        var result = seat.CompleteReactivation(renewalId, "pi_react_1", Guid.Empty, now);

        Assert.True(result.IsSuccess);
        Assert.Equal(SeatStatus.Active, seat.Status);
        Assert.Equal(now, seat.CurrentPeriodStartUtc);
        Assert.Equal(newEnd, seat.CurrentPeriodEndUtc);
        Assert.Equal(newEnd, seat.NextRenewalAtUtc);
        Assert.Null(seat.SuspendedAtUtc);
    }

    [Fact]
    public void CompleteReactivation_on_already_active_seat_is_inert_but_marks_renewal_succeeded()
    {
        // Reentrega del webhook: el seat ya se reactivó, no revertimos ni fallamos.
        var seat = SuspendedSeat(out var suspendedAt);
        var now = suspendedAt.AddHours(2);
        seat.BeginReactivation("seat-reactivation-1", now.AddMonths(1), Guid.Empty, now);
        var renewalId = seat.Renewals.Single().Id;
        seat.CompleteReactivation(renewalId, "pi_react_1", Guid.Empty, now);

        var second = seat.CompleteReactivation(renewalId, "pi_react_1", Guid.Empty, now.AddMinutes(5));

        Assert.True(second.IsSuccess);
        Assert.Equal(SeatStatus.Active, seat.Status);
    }

    private static SubscriptionSeat ActiveSeat()
    {
        var seat = CreateSeat();
        var start = DateTime.UtcNow;
        seat.Activate(start, start.AddMonths(1), Guid.Empty, start);
        return seat;
    }

    private static SubscriptionSeat PastDueSeat()
    {
        var seat = ActiveSeat();
        seat.MarkPastDueBecauseRenewalFailed("card_declined", Guid.Empty, DateTime.UtcNow);
        return seat;
    }

    private static SubscriptionSeat SuspendedSeat(out DateTime suspendedAt)
    {
        var seat = PastDueSeat();
        suspendedAt = DateTime.UtcNow;
        seat.SuspendAfterRetriesExhausted(Guid.Empty, suspendedAt);
        return seat;
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
