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

    // RBAC Fase 2 (RBAC_Hardening_Plan.md) — SystemTenantAdmin deja de ser un god-role dinámico.

    [Theory]
    [InlineData(PermissionCatalog.RolesManage)]
    [InlineData(PermissionCatalog.BillingView)]
    [InlineData(PermissionCatalog.BillingManage)]
    [InlineData(PermissionCatalog.SubscriptionManage)]
    [InlineData(PermissionCatalog.TenantDomainsManage)]
    [InlineData(PermissionCatalog.SignaturePlanConstraintsManage)]
    [InlineData(PermissionCatalog.CloudStorageLegalManage)]
    public void SystemTenantAdmin_does_not_include_dangerous_permissions(string dangerousCode)
    {
        var definition = PermissionCatalog.All.Single(d => d.Code == dangerousCode);
        var tenantAdminDefaults = PermissionCatalog.SystemRoleDefaults(Role.SystemTenantAdmin);

        Assert.True(definition.IsDangerous);
        Assert.DoesNotContain(dangerousCode, tenantAdminDefaults);
    }

    [Fact]
    public void SystemTenantAdmin_still_includes_a_baseline_non_dangerous_permission()
    {
        // Guardarraíl del guardarraíl: confirma que el filtro !IsDangerous no vació el set entero
        // por error — un permiso operativo normal debe seguir llegando por el bundle automático.
        var tenantAdminDefaults = PermissionCatalog.SystemRoleDefaults(Role.SystemTenantAdmin);

        Assert.Contains(PermissionCatalog.CustomersView, tenantAdminDefaults);
        Assert.Contains(PermissionCatalog.CustomersManage, tenantAdminDefaults);
    }

    [Fact]
    public void DmcaCounterNotice_is_deliberately_not_dangerous_despite_looking_like_a_legal_permission()
    {
        // Desviación deliberada del plan original (ver comentario junto a la definición en
        // PermissionCatalog): a diferencia de CloudStorageLegalManage (equipo legal de
        // plataforma), este permiso es la respuesta legal del propio tenant a un takedown
        // recibido sobre SU archivo — con plazos reales de 17 U.S.C. §512(g). Marcarlo
        // IsDangerous dejaría a cualquier oficina sin poder auto-defenderse sin depender de
        // PlatformAdmin.
        var definition = PermissionCatalog.All.Single(d => d.Code == PermissionCatalog.CloudStorageDmcaCounterNotice);
        var tenantAdminDefaults = PermissionCatalog.SystemRoleDefaults(Role.SystemTenantAdmin);

        Assert.False(definition.IsDangerous);
        Assert.Contains(PermissionCatalog.CloudStorageDmcaCounterNotice, tenantAdminDefaults);
    }

    // RBAC Fase 8 (RBAC_Hardening_Plan.md) — migración de [Authorize(Roles=...)] a
    // [HasPermission] en Subscription/Auth-Invitations/Tenant. Los TenantAdmin-only de antes
    // (subscription.plan.change, seats.manage, addons.manage) deben seguir llegando por el
    // bundle automático — la migración exige "más permisivo, no bloquea injustamente", así que
    // el caso positivo (siguen en el default) es tan importante como el negativo (PlatformOnly
    // queda afuera).

    [Theory]
    [InlineData(PermissionCatalog.SubscriptionPlanChange)]
    [InlineData(PermissionCatalog.SeatsManage)]
    [InlineData(PermissionCatalog.AddOnsManage)]
    public void Subscription_tenant_scoped_permissions_reach_tenant_admin_by_default(string code)
    {
        var definition = PermissionCatalog.All.Single(d => d.Code == code);
        var tenantAdminDefaults = PermissionCatalog.SystemRoleDefaults(Role.SystemTenantAdmin);

        Assert.False(definition.PlatformOnly);
        Assert.False(definition.IsDangerous);
        Assert.Contains(code, tenantAdminDefaults);
    }

    [Theory]
    [InlineData(PermissionCatalog.SubscriptionSuspend)]
    [InlineData(PermissionCatalog.SubscriptionReactivate)]
    [InlineData(PermissionCatalog.SubscriptionRenew)]
    [InlineData(PermissionCatalog.SubscriptionAdminCrossTenant)]
    [InlineData(PermissionCatalog.TenantStatusChange)]
    [InlineData(PermissionCatalog.TenantListView)]
    public void Subscription_and_tenant_platform_only_permissions_never_reach_tenant_admin_defaults(string code)
    {
        var definition = PermissionCatalog.All.Single(d => d.Code == code);
        var tenantAdminDefaults = PermissionCatalog.SystemRoleDefaults(Role.SystemTenantAdmin);

        Assert.True(definition.PlatformOnly);
        Assert.False(definition.IsAssignableByTenant);
        Assert.DoesNotContain(code, tenantAdminDefaults);
    }

    [Fact]
    public void Fase8_permissions_have_unique_codes_and_ids()
    {
        var fase8 = PermissionCatalog
            .All.Where(definition => definition.Module is "subscription" or "seats" or "addons")
            .ToArray();

        // 7 nuevas de Subscription (5 "subscription" + seats + addons) — audit.view se reusa,
        // no agrega fila nueva.
        Assert.Equal(7, fase8.Length);
        Assert.Equal(fase8.Length, fase8.Select(definition => definition.Code).Distinct().Count());
        Assert.Equal(fase8.Length, fase8.Select(definition => definition.Id).Distinct().Count());

        var tenantModule = PermissionCatalog.All.Where(definition => definition.Module == "tenant").ToArray();
        Assert.Equal(2, tenantModule.Length);
        Assert.Equal(tenantModule.Length, tenantModule.Select(definition => definition.Code).Distinct().Count());
    }
}
