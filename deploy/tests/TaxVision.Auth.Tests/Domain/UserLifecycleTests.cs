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

        user.Deactivate(DateTime.UtcNow);
        Assert.False(user.IsActive);
        Assert.Equal(UserStatus.Deactivated, user.Status);

        user.Reactivate();
        Assert.True(user.IsActive);
        Assert.Equal(UserStatus.Active, user.Status);
    }

    [Fact]
    public void Offboard_is_terminal()
    {
        var user = NewUser();

        var result = user.Offboard(DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(UserStatus.Offboarded, user.Status);
        Assert.False(user.IsActive);
        Assert.NotNull(user.RemovedAtUtc);
    }

    [Fact]
    public void Offboard_is_idempotent()
    {
        var user = NewUser();
        user.Offboard(DateTime.UtcNow);
        var removedAt = user.RemovedAtUtc;

        var second = user.Offboard(DateTime.UtcNow.AddMinutes(5));

        Assert.True(second.IsSuccess);
        Assert.Equal(UserStatus.Offboarded, user.Status);
        Assert.Equal(removedAt, user.RemovedAtUtc);
    }

    [Fact]
    public void Deactivate_does_not_downgrade_an_offboarded_user()
    {
        var user = NewUser();
        user.Offboard(DateTime.UtcNow);

        user.Deactivate(DateTime.UtcNow);

        Assert.Equal(UserStatus.Offboarded, user.Status);
        Assert.False(user.IsActive);
    }
}
