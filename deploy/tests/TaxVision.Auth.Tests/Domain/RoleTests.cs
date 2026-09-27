using TaxVision.Auth.Domain.Roles;

namespace TaxVision.Auth.Tests.Domain;

public sealed class RoleTests
{
    [Theory]
    [InlineData("PlatformAdmin")]
    [InlineData("platform admin")]
    [InlineData("Platform-Admin")]
    [InlineData("PlаtformAdmin")]
    [InlineData("TenantAdmin")]
    [InlineData("Tenant Admin")]
    [InlineData("Employee")]
    [InlineData("Customer Portal")]
    public void Create_rejects_a_custom_role_whose_name_is_reserved(string name)
    {
        var result = Role.Create(Guid.NewGuid(), name, "Intento de suplantación");

        Assert.True(result.IsFailure);
        Assert.Equal("Role.NameReserved", result.Error.Code);
    }

    [Theory]
    [InlineData(Role.SystemTenantAdmin)]
    [InlineData(Role.SystemEmployee)]
    [InlineData(Role.SystemCustomerPortal)]
    public void Create_still_seeds_the_system_roles_with_their_reserved_names(string name)
    {
        var result = Role.Create(Guid.NewGuid(), name, "System role", isSystem: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(name, result.Value.Name);
    }

    [Fact]
    public void Create_accepts_a_legitimate_custom_name()
    {
        var result = Role.Create(Guid.NewGuid(), "Senior Preparer", "Staff role");

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsSystem);
    }

    [Theory]
    [InlineData("PlatformAdmin")]
    [InlineData("Platform_Admin")]
    [InlineData("customerportal")]
    public void Update_rejects_renaming_a_custom_role_to_a_reserved_name(string name)
    {
        var role = Role.Create(Guid.NewGuid(), "Front Desk", null).Value;

        var result = role.Update(name, null);

        Assert.True(result.IsFailure);
        Assert.Equal("Role.NameReserved", result.Error.Code);
        Assert.Equal("Front Desk", role.Name);
    }

    [Fact]
    public void Update_renames_a_custom_role_to_a_free_name()
    {
        var role = Role.Create(Guid.NewGuid(), "Front Desk", null).Value;

        var result = role.Update("Reception", "Atiende la recepción");

        Assert.True(result.IsSuccess);
        Assert.Equal("Reception", role.Name);
    }

    [Fact]
    public void Create_keeps_rejecting_a_name_outside_the_length_bounds_before_the_reserved_check()
    {
        var result = Role.Create(Guid.NewGuid(), "A", null);

        Assert.True(result.IsFailure);
        Assert.Equal("Role.Name", result.Error.Code);
    }
}
