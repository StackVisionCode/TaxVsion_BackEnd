using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.RateLimiting;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.ActorTypeAuthorization;

/// <summary>
/// Política de actor/tenant de la fuente genérica del gate de módulo (Fase 1). En log-only cualquier
/// caso indeterminado hace fail-open (no gatea); solo un tenant con proyección cuyo módulo NO está en
/// la lista devuelve <c>false</c>.
/// </summary>
public sealed class TenantModuleEntitlementsSourceTests
{
    private const string Module = "signatures";
    private static readonly Guid Tenant = Guid.NewGuid();

    private static TenantModuleEntitlementsSource Source(FakeReader reader) => new(reader);

    [Fact]
    public async Task Enabled_when_module_is_in_the_tenants_list()
    {
        var source = Source(new FakeReader(["signatures", "documents"]));
        Assert.True(await source.IsModuleEnabledAsync(Staff(Tenant), Module));
    }

    [Fact]
    public async Task Disabled_when_module_is_not_in_the_tenants_list()
    {
        var source = Source(new FakeReader(["documents"]));
        Assert.False(await source.IsModuleEnabledAsync(Staff(Tenant), Module));
    }

    [Fact]
    public async Task Module_match_is_case_insensitive()
    {
        var source = Source(new FakeReader(["Signatures"]));
        Assert.True(await source.IsModuleEnabledAsync(Staff(Tenant), Module));
    }

    [Fact]
    public async Task PlatformAdmin_is_always_enabled_even_without_the_module()
    {
        var source = Source(new FakeReader(["documents"]));
        Assert.True(await source.IsModuleEnabledAsync(PlatformAdmin(Tenant), Module));
    }

    [Fact]
    public async Task Service_actor_is_always_enabled()
    {
        var source = Source(new FakeReader(["documents"]));
        Assert.True(await source.IsModuleEnabledAsync(Actor(ActorType.Service, Tenant), Module));
    }

    [Fact]
    public async Task No_tenant_in_token_does_not_gate()
    {
        var source = Source(new FakeReader(["documents"]));
        var noTenant = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimNames.ActorType, ActorType.TenantEmployee.ToString())], "Test")
        );
        Assert.True(await source.IsModuleEnabledAsync(noTenant, Module));
    }

    [Fact]
    public async Task Missing_projection_does_not_gate()
    {
        // null = proyección aún inexistente (consistencia eventual) → no gatear (evita falso deny).
        var source = Source(new FakeReader(null));
        Assert.True(await source.IsModuleEnabledAsync(Staff(Tenant), Module));
    }

    // ------------------------------------------------------------------

    private static ClaimsPrincipal Staff(Guid tenantId) => Actor(ActorType.TenantEmployee, tenantId);

    private static ClaimsPrincipal Actor(ActorType actorType, Guid tenantId) =>
        new(
            new ClaimsIdentity(
                [
                    new Claim(ClaimNames.ActorType, actorType.ToString()),
                    new Claim(ClaimNames.TenantId, tenantId.ToString()),
                ],
                "Test"
            )
        );

    private static ClaimsPrincipal PlatformAdmin(Guid tenantId) =>
        new(
            new ClaimsIdentity(
                [
                    new Claim(ClaimNames.ActorType, ActorType.PlatformAdmin.ToString()),
                    new Claim(ClaimNames.TenantId, tenantId.ToString()),
                    new Claim(ClaimTypes.Role, "PlatformAdmin"),
                ],
                "Test"
            )
        );

    // ---------- A6: una lista VACÍA significa "ningún módulo" ----------

    [Fact]
    public async Task An_empty_projection_denies()
    {
        // Verificado contra la base: el tenant con la lista vacía tiene su suscripción `Expired` y
        // los módulos de su plan a "false". El `[]` es la traducción correcta de eso.
        //
        // Este test existe porque la trampa es tentadora: tratar la lista vacía como "no sé" —igual
        // que la ausencia de fila— le daría acceso COMPLETO a un tenant vencido cuando el gate pase
        // a enforce.
        var source = Source(new FakeReader([]));

        Assert.False(await source.IsModuleEnabledAsync(Staff(Tenant), Module));
    }

    [Fact]
    public async Task A_missing_projection_is_NOT_the_same_as_an_empty_one()
    {
        // Sin fila = todavía no se sabe (consistencia eventual) → pasa.
        // Lista vacía = se sabe que no tiene nada → deniega.
        var missing = Source(new FakeReader(null));
        var empty = Source(new FakeReader([]));

        Assert.True(await missing.IsModuleEnabledAsync(Staff(Tenant), Module));
        Assert.False(await empty.IsModuleEnabledAsync(Staff(Tenant), Module));
    }

    private sealed class FakeReader(IReadOnlyList<string>? modules) : ITenantEntitlementModulesReader
    {
        public Task<IReadOnlyList<string>?> GetEnabledModulesAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(modules);
    }
}
