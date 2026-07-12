using BuildingBlocks.Authorization;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Domain;

public sealed class AuthDomainTests
{
    [Fact]
    public void Customer_portal_registration_requires_customer_id()
    {
        var result = User.Register(
            Guid.NewGuid(),
            "Portal",
            "User",
            "portal@example.com",
            "password-hash",
            UserActorType.CustomerPortal
        );

        Assert.True(result.IsFailure);
        Assert.Equal("User.Customer", result.Error.Code);
    }

    [Fact]
    public void Repeated_failed_logins_lock_the_user()
    {
        var user = User.Register(
            Guid.NewGuid(),
            "Tax",
            "Professional",
            "user@example.com",
            "password-hash",
            UserActorType.TenantEmployee
        ).Value;
        var now = DateTime.UtcNow;

        user.RegisterFailedLogin(now, 3, TimeSpan.FromMinutes(15));
        user.RegisterFailedLogin(now, 3, TimeSpan.FromMinutes(15));
        user.RegisterFailedLogin(now, 3, TimeSpan.FromMinutes(15));

        Assert.True(user.IsLockedOut(now.AddMinutes(1)));
        Assert.Equal(0, user.FailedLoginCount);
    }

    [Fact]
    public void System_roles_are_immutable()
    {
        var role = Role.Create(Guid.NewGuid(), Role.SystemTenantAdmin, "System role", isSystem: true).Value;

        var result = role.Update("Changed", null);

        Assert.True(result.IsFailure);
        Assert.Equal("Role.System", result.Error.Code);
    }

    [Fact]
    public void Employee_defaults_include_scoped_cloudstorage_actions()
    {
        var defaults = PermissionCatalog.SystemRoleDefaults(Role.SystemEmployee);

        Assert.Contains(CloudStoragePermissions.FileView, defaults);
        Assert.Contains(CloudStoragePermissions.FileUpload, defaults);
        Assert.Contains(CloudStoragePermissions.FileDownload, defaults);
        Assert.DoesNotContain(CloudStoragePermissions.FileDelete, defaults);
    }
}
