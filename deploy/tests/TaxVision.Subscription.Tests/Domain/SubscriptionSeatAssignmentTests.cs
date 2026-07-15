using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

public sealed class SubscriptionSeatAssignmentTests
{
    [Fact]
    public void AssignTo_moves_an_available_seat_to_assigned()
    {
        var seat = CreateSeat();
        var userId = Guid.NewGuid();

        var result = seat.AssignTo(userId, Guid.Empty, DateTime.UtcNow, reassignmentCooldownDays: 0);

        Assert.True(result.IsSuccess);
        Assert.Equal(SeatStatus.Assigned, seat.Status);
        Assert.Equal(userId, seat.CurrentUserId);
        Assert.Single(seat.Assignments);
    }

    [Fact]
    public void AssignTo_a_seat_already_assigned_fails()
    {
        var seat = CreateSeat();
        seat.AssignTo(Guid.NewGuid(), Guid.Empty, DateTime.UtcNow, 0);

        var result = seat.AssignTo(Guid.NewGuid(), Guid.Empty, DateTime.UtcNow, 0);

        Assert.True(result.IsFailure);
        Assert.Equal("Seat.NotAvailable", result.Error.Code);
    }

    [Fact]
    public void ReleaseCurrentAssignment_returns_an_assigned_seat_to_available()
    {
        var seat = CreateSeat();
        seat.AssignTo(Guid.NewGuid(), Guid.Empty, DateTime.UtcNow, 0);

        var result = seat.ReleaseCurrentAssignment(Guid.Empty, DateTime.UtcNow, "no longer needed");

        Assert.True(result.IsSuccess);
        Assert.Equal(SeatStatus.Available, seat.Status);
        Assert.Null(seat.CurrentUserId);
    }

    [Fact]
    public void ReleaseCurrentAssignment_without_an_active_assignment_fails()
    {
        var seat = CreateSeat();

        var result = seat.ReleaseCurrentAssignment(Guid.Empty, DateTime.UtcNow, reason: null);

        Assert.True(result.IsFailure);
        Assert.Equal("Seat.NotAssigned", result.Error.Code);
    }

    [Fact]
    public void ReassignSeat_replaces_the_active_assignment_with_a_new_user()
    {
        var seat = CreateSeat();
        var firstUser = Guid.NewGuid();
        var secondUser = Guid.NewGuid();
        seat.AssignTo(firstUser, Guid.Empty, DateTime.UtcNow, 0);

        var result = seat.ReassignSeat(
            secondUser,
            Guid.Empty,
            DateTime.UtcNow,
            "reassigned",
            reassignmentCooldownDays: 0
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(secondUser, seat.CurrentUserId);
        Assert.Equal(2, seat.Assignments.Count);
    }

    [Fact]
    public void AssignTo_respects_the_reassignment_cooldown()
    {
        var seat = CreateSeat();
        var now = DateTime.UtcNow;
        seat.AssignTo(Guid.NewGuid(), Guid.Empty, now, 0);
        seat.ReleaseCurrentAssignment(Guid.Empty, now, reason: null);

        var result = seat.AssignTo(Guid.NewGuid(), Guid.Empty, now.AddHours(1), reassignmentCooldownDays: 7);

        Assert.True(result.IsFailure);
        Assert.Equal("Seat.ReassignmentCooldown", result.Error.Code);
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
