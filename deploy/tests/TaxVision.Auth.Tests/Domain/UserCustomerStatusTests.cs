using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Domain;

/// <summary>CUS-10 (D-POR-1): la cuenta de portal sigue el archivado y la restauración del cliente.</summary>
public sealed class UserCustomerStatusTests
{
    private static readonly DateTime T0 = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Archived_DeactivatesThePortalAccount_WithItsReason()
    {
        var user = PortalUser();

        var result = user.ApplyCustomerArchived(T0, T0);

        Assert.True(result.IsSuccess);
        Assert.False(user.IsActive);
        Assert.Equal(UserStatus.Deactivated, user.Status);
        Assert.Equal(UserDeactivationReason.CustomerArchived, user.DeactivationReason);
    }

    [Fact]
    public void Restored_ReactivatesWhatTheArchiveCut()
    {
        var user = PortalUser();
        user.ApplyCustomerArchived(T0, T0);

        user.ApplyCustomerRestored(T0.AddMinutes(1), T0.AddMinutes(1));

        Assert.True(user.IsActive);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Null(user.DeactivationReason);
    }

    [Fact]
    public void Restored_LeavesAnAccountTheOfficeDeactivated()
    {
        var user = PortalUser();
        user.Deactivate(Guid.NewGuid(), T0);

        user.ApplyCustomerArchived(T0.AddMinutes(1), T0.AddMinutes(1));
        user.ApplyCustomerRestored(T0.AddMinutes(2), T0.AddMinutes(2));

        Assert.False(user.IsActive);
        Assert.Equal(UserDeactivationReason.Admin, user.DeactivationReason);
    }

    [Fact]
    public void Restored_LeavesAnAccountWithoutAReason()
    {
        // Filas anteriores a CUS-10: sin motivo se tratan como desactivadas por la oficina.
        var user = PortalUser();
        user.ApplyCustomerArchived(T0, T0);
        typeof(User).GetProperty(nameof(User.DeactivationReason))!.SetValue(user, null);

        user.ApplyCustomerRestored(T0.AddMinutes(1), T0.AddMinutes(1));

        Assert.False(user.IsActive);
    }

    [Fact]
    public void LateArchive_AfterANewerRestore_ChangesNothing()
    {
        var user = PortalUser();

        user.ApplyCustomerRestored(T0.AddMinutes(5), T0.AddMinutes(6));
        user.ApplyCustomerArchived(T0, T0.AddMinutes(7));

        Assert.True(user.IsActive);
    }

    [Fact]
    public void LateRestore_AfterANewerArchive_ChangesNothing()
    {
        var user = PortalUser();
        user.ApplyCustomerArchived(T0, T0);
        user.ApplyCustomerRestored(T0.AddMinutes(1), T0.AddMinutes(1));
        user.ApplyCustomerArchived(T0.AddMinutes(3), T0.AddMinutes(3));

        user.ApplyCustomerRestored(T0.AddMinutes(1), T0.AddMinutes(4));

        Assert.False(user.IsActive);
    }

    [Fact]
    public void RepeatedArchive_IsIdempotent()
    {
        var user = PortalUser();
        user.ApplyCustomerArchived(T0, T0);
        var deactivatedAt = user.DeactivatedAtUtc;

        var again = user.ApplyCustomerArchived(T0, T0.AddMinutes(1));

        Assert.True(again.IsSuccess);
        Assert.False(user.IsActive);
        Assert.Equal(deactivatedAt, user.DeactivatedAtUtc);
    }

    [Fact]
    public void StaffAccount_IsRefused()
    {
        var user = User.Register(
            Guid.NewGuid(),
            "Erin",
            "Staff",
            "erin@example.test",
            "hash",
            UserActorType.TenantEmployee
        ).Value;

        var result = user.ApplyCustomerArchived(T0, T0);

        Assert.Equal("User.NotPortal", result.Error.Code);
        Assert.True(user.IsActive);
    }

    [Fact]
    public void AdminDeactivation_RecordsAdminReason_AndReactivationClearsIt()
    {
        var user = PortalUser();

        user.Deactivate(Guid.NewGuid(), T0);
        Assert.Equal(UserDeactivationReason.Admin, user.DeactivationReason);

        user.Reactivate(T0.AddMinutes(1));
        Assert.Null(user.DeactivationReason);
    }

    private static User PortalUser() =>
        User.Register(
            Guid.NewGuid(),
            "Paula",
            "Client",
            "paula@example.test",
            "hash",
            UserActorType.CustomerPortal,
            Guid.NewGuid()
        ).Value;
}
