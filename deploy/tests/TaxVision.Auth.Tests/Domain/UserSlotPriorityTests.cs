using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Domain.Users.Events;

namespace TaxVision.Auth.Tests.Domain;

public sealed class UserSlotPriorityTests
{
    [Fact]
    public void Owner_starts_first_and_cannot_be_moved()
    {
        var owner = User.Register(
            Guid.NewGuid(),
            "Ada",
            "Owner",
            "owner@example.com",
            "hash",
            UserActorType.TenantAdmin,
            onboardingId: Guid.NewGuid()
        ).Value;

        Assert.True(owner.IsOwner);
        Assert.Equal(DateTime.MinValue, owner.SlotPriorityAt);
        Assert.True(owner.OverrideSlotPriority(DateTime.UtcNow, Guid.NewGuid(), "Move owner").IsFailure);
        Assert.True(owner.OverrideSlotPriority(null, Guid.NewGuid(), "Release owner").IsFailure);
    }

    [Fact]
    public void Assign_if_missing_does_not_replace_existing_priority()
    {
        var user = Staff();
        var initial = user.SlotPriorityAt;

        var result = user.AssignSlotPriorityIfMissing(DateTime.UtcNow.AddDays(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(initial, user.SlotPriorityAt);
        Assert.Empty(user.DomainEvents);
    }

    [Fact]
    public void Deactivation_releases_priority_and_emits_event()
    {
        var user = Staff();

        user.Deactivate(DateTime.UtcNow);

        Assert.Null(user.SlotPriorityAt);
        Assert.Contains(user.DomainEvents, evt => evt is UserSlotPriorityReleased);
    }

    [Fact]
    public void Reactivation_assigns_new_priority_and_emits_event()
    {
        var user = Staff();
        user.Deactivate(DateTime.UtcNow);
        user.ClearDomainEvents();
        var now = DateTime.UtcNow.AddMinutes(1);

        user.Reactivate(now);

        Assert.Equal(now, user.SlotPriorityAt);
        Assert.Contains(user.DomainEvents, evt => evt is UserSlotPriorityAssigned);
    }

    [Fact]
    public void Override_requires_reason()
    {
        var result = Staff().OverrideSlotPriority(DateTime.UtcNow, Guid.NewGuid(), " ");

        Assert.True(result.IsFailure);
        Assert.Equal("User.SlotOverrideReason", result.Error.Code);
    }

    [Fact]
    public void Portal_never_occupies_or_overrides_slot()
    {
        var portal = User.Register(
            Guid.NewGuid(),
            "Client",
            "Portal",
            "client@example.com",
            "hash",
            UserActorType.CustomerPortal,
            customerId: Guid.NewGuid()
        ).Value;

        Assert.Null(portal.SlotPriorityAt);
        Assert.True(portal.OverrideSlotPriority(DateTime.UtcNow, Guid.NewGuid(), "Invalid").IsFailure);
    }

    [Fact]
    public void Override_changes_priority_and_records_audit_context_event()
    {
        var user = Staff();
        var actorId = Guid.NewGuid();
        var value = DateTime.UtcNow.AddHours(-1);

        var result = user.OverrideSlotPriority(value, actorId, "Senior lead");

        Assert.True(result.IsSuccess);
        var evt = Assert.IsType<UserSlotPriorityOverridden>(Assert.Single(user.DomainEvents));
        Assert.Equal(actorId, evt.ActorAdminId);
        Assert.Equal("Senior lead", evt.Reason);
        Assert.Equal(value, evt.NewAt);
    }

    private static User Staff() =>
        User.Register(Guid.NewGuid(), "Ana", "Staff", "staff@example.com", "hash", UserActorType.TenantEmployee).Value;
}
