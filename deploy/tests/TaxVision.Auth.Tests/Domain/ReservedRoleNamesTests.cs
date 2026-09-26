using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Domain;

public sealed class ReservedRoleNamesTests
{
    public static TheoryData<string> PlatformAdminVariants =>
        new()
        {
            "PlatformAdmin",
            "platformadmin",
            "PLATFORMADMIN",
            "Platform Admin",
            "  platform   admin  ",
            "Platform-Admin",
            "Platform_Admin",
            "Platform.Admin",
            "Platform­Admin", // guion suave invisible
            "Platform​Admin", // espacio de ancho cero
            "PlаtformAdmin", // "а" cirílica
            "РlatformAdmin", // "Р" cirílica
            "PlatformAdmiи", // ojo: "и" cirílica NO es homoglifo de "n"
            "ＰlatformAdmin", // "Ｐ" de ancho completo, la unifica NFKC
        };

    [Theory]
    [MemberData(nameof(PlatformAdminVariants))]
    public void IsReserved_catches_every_normalized_variant_of_PlatformAdmin(string candidate)
    {
        // La última variante lleva una "и" cirílica, que no se parece a una "n" latina: se deja
        // fuera del mapa a propósito y por eso este caso NO debe quedar reservado.
        var expected = candidate != "PlatformAdmiи";

        Assert.Equal(expected, ReservedRoleNames.IsReserved(candidate));
    }

    [Fact]
    public void IsReserved_covers_the_actor_type_pseudo_roles_and_the_system_role_names()
    {
        foreach (var name in ReservedRoleNames.DisplayNames)
            Assert.True(ReservedRoleNames.IsReserved(name), $"'{name}' debería estar reservado.");
    }

    [Fact]
    public void IsReserved_covers_every_actor_type_even_if_a_new_one_appears()
    {
        foreach (var actorType in Enum.GetValues<UserActorType>())
            Assert.True(ReservedRoleNames.IsReserved(UserActorRoles.For(actorType)));
    }

    [Theory]
    [InlineData("Front Desk")]
    [InlineData("Senior Preparer")]
    [InlineData("Tenant Administrator")] // contiene el reservado, pero no es el reservado
    [InlineData("Employee of the Month")]
    [InlineData("Portal Customer")] // mismas palabras, otro orden
    public void IsReserved_leaves_legitimate_custom_names_alone(string candidate)
    {
        Assert.False(ReservedRoleNames.IsReserved(candidate));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("---")]
    public void Normalize_returns_empty_for_names_without_alphanumerics(string? candidate)
    {
        Assert.Equal(string.Empty, ReservedRoleNames.Normalize(candidate));
        Assert.False(ReservedRoleNames.IsReserved(candidate));
    }

    [Fact]
    public void Normalize_collapses_separators_and_case()
    {
        Assert.Equal("tenantadmin", ReservedRoleNames.Normalize(Role.SystemTenantAdmin));
        Assert.Equal("tenantadmin", ReservedRoleNames.Normalize("tenant-admin"));
        Assert.Equal("customerportal", ReservedRoleNames.Normalize(Role.SystemCustomerPortal));
    }
}
