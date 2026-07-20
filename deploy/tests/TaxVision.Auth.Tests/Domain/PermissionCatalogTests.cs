using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Tenants;

namespace TaxVision.Auth.Tests.Domain;

/// <summary>
/// Fase A0 — cubre los metadatos nuevos del catálogo (MinPlanTier/IsAssignableByTenant) y la
/// reconciliación de los 18 permisos de Communication que antes solo existían en BD.
/// </summary>
public sealed class PermissionCatalogTests
{
    [Theory]
    [InlineData(PermissionCatalog.BillingView)]
    [InlineData(PermissionCatalog.BillingManage)]
    [InlineData(PermissionCatalog.SubscriptionManage)]
    [InlineData(PermissionCatalog.RolesManage)]
    public void Reserved_permissions_are_never_assignable_by_a_tenant(string code)
    {
        var definition = PermissionCatalog.All.Single(d => d.Code == code);

        Assert.False(definition.IsAssignableByTenant);
    }

    [Theory]
    [InlineData(PermissionCatalog.CustomersView)]
    [InlineData(PermissionCatalog.CloudStorageFileUpload)]
    [InlineData(PermissionCatalog.SignatureRequestCreate)]
    public void Baseline_permissions_are_assignable_from_the_starter_plan(string code)
    {
        var definition = PermissionCatalog.All.Single(d => d.Code == code);

        Assert.True(definition.IsAssignableByTenant);
        Assert.Equal((int)PlanTier.Starter, definition.MinPlanTier);
    }

    [Theory]
    [InlineData(PermissionCatalog.EmailUse)]
    [InlineData(PermissionCatalog.CommsCalls)]
    [InlineData(PermissionCatalog.CampaignsManage)]
    [InlineData(PermissionCatalog.ReportsView)]
    public void Pro_only_modules_require_at_least_the_pro_tier(string code)
    {
        var definition = PermissionCatalog.All.Single(d => d.Code == code);

        Assert.Equal((int)PlanTier.Pro, definition.MinPlanTier);
    }

    [Fact]
    public void Communication_permissions_are_reconciled_into_the_catalog_with_the_original_guids()
    {
        // Los mismos 18 GUID que sembró la migración AddCommunicationPermissions por SQL
        // directo (2026-07-10) — deben resolver a los mismos Ids, nunca duplicarse.
        var expectedIds = Enumerable.Range(45, 18).Select(n => new Guid($"a1000000-0000-0000-0000-{n:D12}")).ToList();

        var actualIds = PermissionCatalog
            .All.Where(definition => definition.Module == "communication")
            .Select(definition => definition.Id)
            .OrderBy(id => id)
            .ToList();

        Assert.Equal(expectedIds.OrderBy(id => id), actualIds);
        Assert.All(
            PermissionCatalog.All.Where(definition => definition.Module == "communication"),
            definition => Assert.Equal((int)PlanTier.Pro, definition.MinPlanTier)
        );
    }

    [Fact]
    public void Employee_defaults_now_include_the_reconciled_communication_permissions()
    {
        var defaults = PermissionCatalog.SystemRoleDefaults(Role.SystemEmployee);

        Assert.Contains(PermissionCatalog.CommunicationChatStart, defaults);
        Assert.Contains(PermissionCatalog.CommunicationMeetingJoin, defaults);
        Assert.DoesNotContain(PermissionCatalog.CommunicationSettingsManage, defaults);
        Assert.DoesNotContain(PermissionCatalog.CommunicationAnalyticsRead, defaults);
    }

    [Fact]
    public void Customer_portal_defaults_never_include_moderation_or_admin_communication_permissions()
    {
        var defaults = PermissionCatalog.SystemRoleDefaults(Role.SystemCustomerPortal);

        Assert.Contains(PermissionCatalog.CommunicationChatStart, defaults);
        Assert.DoesNotContain(PermissionCatalog.CommunicationChatModerate, defaults);
        Assert.DoesNotContain(PermissionCatalog.CommunicationMeetingHost, defaults);
        Assert.DoesNotContain(PermissionCatalog.CommunicationSettingsManage, defaults);
    }

    [Fact]
    public void Growth_cross_tenant_permission_is_platform_only_and_never_in_tenant_admin_defaults()
    {
        var definition = PermissionCatalog.All.Single(d => d.Code == PermissionCatalog.GrowthAdminCrossTenant);
        var tenantAdminDefaults = PermissionCatalog.SystemRoleDefaults(Role.SystemTenantAdmin);

        Assert.True(definition.PlatformOnly);
        Assert.False(definition.IsAssignableByTenant);
        Assert.DoesNotContain(PermissionCatalog.GrowthAdminCrossTenant, tenantAdminDefaults);
    }

    [Fact]
    public void Growth_permissions_have_unique_codes_and_ids()
    {
        var growth = PermissionCatalog
            .All.Where(definition => definition.Module is "growth" or "codes" or "referrals")
            .ToArray();

        Assert.Equal(18, growth.Length);
        Assert.Equal(growth.Length, growth.Select(definition => definition.Code).Distinct().Count());
        Assert.Equal(growth.Length, growth.Select(definition => definition.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("starter", PlanTier.Starter)]
    [InlineData("PRO", PlanTier.Pro)]
    [InlineData("Enterprise", PlanTier.Enterprise)]
    [InlineData(null, PlanTier.Starter)]
    [InlineData("unknown-plan", PlanTier.Starter)]
    public void PlanTierResolver_fails_closed_to_starter_for_unknown_or_missing_plan_codes(
        string? planCode,
        PlanTier expected
    )
    {
        Assert.Equal(expected, PlanTierResolver.FromPlanCode(planCode));
    }
}
