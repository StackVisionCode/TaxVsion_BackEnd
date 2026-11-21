using BuildingBlocks.Authorization;
using TaxVision.Auth.Domain.Roles;
using Xunit;

namespace TaxVision.Auth.Tests.Domain;

/// <summary>El permiso de File Library solo abre la página: delegable, en admin y empleado, nunca en el portal.</summary>
public sealed class PermissionCatalogLibraryTests
{
    private static PermissionCatalog.PermissionDefinition Library() =>
        Assert.Single(PermissionCatalog.All, p => p.Code == "cloudstorage.library.view");

    [Fact]
    public void The_library_permission_exists_with_its_own_guid()
    {
        var permission = Library();

        Assert.Equal(new Guid("a1000000-0000-0000-0000-000000000232"), permission.Id);
        Assert.Equal("Use the file library", permission.Description);
        Assert.False(permission.IsDangerous);
        Assert.False(permission.IsReserved);
        Assert.False(permission.PlatformOnly);
        Assert.False(permission.IsCustomerPortal);
    }

    [Fact]
    public void Admin_and_employee_get_it_by_default_but_not_the_portal()
    {
        Assert.Contains(
            PermissionCatalog.CloudStorageLibraryView,
            PermissionCatalog.SystemRoleDefaults(Role.SystemTenantAdmin)
        );
        Assert.Contains(
            PermissionCatalog.CloudStorageLibraryView,
            PermissionCatalog.SystemRoleDefaults(Role.SystemEmployee)
        );
        Assert.DoesNotContain(
            PermissionCatalog.CloudStorageLibraryView,
            PermissionCatalog.SystemRoleDefaults(Role.SystemCustomerPortal)
        );
    }

    [Fact]
    public void It_belongs_to_the_documents_module()
    {
        Assert.Equal("documents", PermissionModuleMap.ModuleFor(CloudStoragePermissions.LibraryView));
    }
}
