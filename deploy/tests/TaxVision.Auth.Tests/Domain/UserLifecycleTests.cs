using TaxVision.Auth.Domain.Users;
using Xunit;

namespace TaxVision.Auth.Tests.Domain;

/// <summary>Transiciones de ciclo de vida del usuario: deactivate reversible, offboard terminal.</summary>
public sealed class UserLifecycleTests
{
    private static User NewUser() =>
        User.Register(Guid.NewGuid(), "Ana", "Perez", "ana@example.com", "hash", UserActorType.TenantEmployee).Value;

    [Fact]
    public void New_user_is_active()
    {
        var user = NewUser();
        Assert.True(user.IsActive);
        Assert.Equal(UserStatus.Active, user.Status);
    }

    [Fact]
    public void Deactivate_is_reversible()
    {
        var user = NewUser();

        user.Deactivate(Guid.NewGuid(), DateTime.UtcNow);
        Assert.False(user.IsActive);
        Assert.Equal(UserStatus.Deactivated, user.Status);

        user.Reactivate(DateTime.UtcNow);
        Assert.True(user.IsActive);
        Assert.Equal(UserStatus.Active, user.Status);
    }

    [Fact]
    public void Offboard_is_terminal()
    {
        var user = NewUser();

        var result = user.Offboard(Guid.NewGuid(), DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(UserStatus.Offboarded, user.Status);
        Assert.False(user.IsActive);
        Assert.NotNull(user.RemovedAtUtc);
    }

    [Fact]
    public void Offboard_is_idempotent()
    {
        var user = NewUser();
        user.Offboard(Guid.NewGuid(), DateTime.UtcNow);
        var removedAt = user.RemovedAtUtc;

        var second = user.Offboard(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(5));

        Assert.True(second.IsSuccess);
        Assert.Equal(UserStatus.Offboarded, user.Status);
        Assert.Equal(removedAt, user.RemovedAtUtc);
    }

    [Fact]
    public void Deactivate_does_not_downgrade_an_offboarded_user()
    {
        var user = NewUser();
        user.Offboard(Guid.NewGuid(), DateTime.UtcNow);

        user.Deactivate(Guid.NewGuid(), DateTime.UtcNow);

        Assert.Equal(UserStatus.Offboarded, user.Status);
        Assert.False(user.IsActive);
    }

    [Fact]
    public void Reactivate_Offboarded_ReturnsFailure()
    {
        var user = NewUser();
        user.Offboard(Guid.NewGuid(), DateTime.UtcNow);

        var result = user.Reactivate(DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("User.Offboarded", result.Error.Code);
        Assert.Equal(UserStatus.Offboarded, user.Status);
        Assert.False(user.IsActive);
    }

    [Fact]
    public void Deactivate_BySelf_ReturnsFailure()
    {
        var user = NewUser();

        var result = user.Deactivate(user.Id, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("User.SelfAction", result.Error.Code);
        Assert.Equal("You cannot deactivate your own account.", result.Error.Message);
        Assert.True(user.IsActive);
        Assert.Equal(UserStatus.Active, user.Status);
    }

    [Fact]
    public void Offboard_BySelf_ReturnsFailure()
    {
        var user = NewUser();

        var result = user.Offboard(user.Id, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("User.SelfAction", result.Error.Code);
        Assert.Equal("You cannot remove your own account.", result.Error.Message);
        Assert.True(user.IsActive);
        Assert.Equal(UserStatus.Active, user.Status);
    }

    private static User NewOwner() =>
        User.Register(
            Guid.NewGuid(),
            "Ada",
            "Owner",
            "owner@example.com",
            "hash",
            UserActorType.TenantAdmin,
            onboardingId: Guid.NewGuid()
        ).Value;

    [Fact]
    public void Offboard_Owner_ReturnsOwnerSlotFailureAndKeepsState()
    {
        var owner = NewOwner();

        var result = owner.Offboard(Guid.NewGuid(), DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("User.OwnerSlot", result.Error.Code);
        Assert.Equal(UserStatus.Active, owner.Status);
        Assert.True(owner.IsActive);
        Assert.True(owner.IsOwner);
        Assert.Null(owner.RemovedAtUtc);
    }

    [Fact]
    public void Deactivate_Owner_ReturnsOwnerSlotFailure()
    {
        var owner = NewOwner();

        var result = owner.Deactivate(Guid.NewGuid(), DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("User.OwnerSlot", result.Error.Code);
        Assert.Equal(UserStatus.Active, owner.Status);
        Assert.True(owner.IsActive);
        Assert.Null(owner.DeactivatedAtUtc);
    }

    // La compensación de un onboarding cancelado sí apaga al dueño: su oficina no llegó a existir.
    [Fact]
    public void DeactivateForCancelledOnboarding_Owner_TurnsOffTheUserAndKeepsTheSlot()
    {
        var owner = NewOwner();

        owner.DeactivateForCancelledOnboarding(DateTime.UtcNow);

        Assert.False(owner.IsActive);
        Assert.Equal(UserStatus.Deactivated, owner.Status);
        Assert.Equal(DateTime.MinValue, owner.SlotPriorityAt);
    }
}
