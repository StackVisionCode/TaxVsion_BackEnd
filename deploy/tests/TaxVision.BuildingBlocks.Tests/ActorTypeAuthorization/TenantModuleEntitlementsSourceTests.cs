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

    [Fact]
    public async Task Enabled_when_module_is_in_the_tenants_list()
    {
        var source = new TenantModuleEntitlementsSource(new FakeReader(["signatures", "documents"]));
        Assert.True(await source.IsModuleEnabledAsync(Staff(Tenant), Module));
    }

    [Fact]
    public async Task Disabled_when_module_is_not_in_the_tenants_list()
    {
        var source = new TenantModuleEntitlementsSource(new FakeReader(["documents"]));
        Assert.False(await source.IsModuleEnabledAsync(Staff(Tenant), Module));
    }

    [Fact]
    public async Task Module_match_is_case_insensitive()
    {
        var source = new TenantModuleEntitlementsSource(new FakeReader(["Signatures"]));
        Assert.True(await source.IsModuleEnabledAsync(Staff(Tenant), Module));
    }

    [Fact]
    public async Task PlatformAdmin_is_always_enabled_even_without_the_module()
    {
        var source = new TenantModuleEntitlementsSource(new FakeReader(["documents"]));
        Assert.True(await source.IsModuleEnabledAsync(PlatformAdmin(Tenant), Module));
    }

    [Fact]
    public async Task Service_actor_is_always_enabled()
    {
        var source = new TenantModuleEntitlementsSource(new FakeReader(["documents"]));
        Assert.True(await source.IsModuleEnabledAsync(Actor(ActorType.Service, Tenant), Module));
    }

    [Fact]
    public async Task No_tenant_in_token_does_not_gate()
    {
        var source = new TenantModuleEntitlementsSource(new FakeReader(["documents"]));
        var noTenant = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimNames.ActorType, ActorType.TenantEmployee.ToString())], "Test")
        );
        Assert.True(await source.IsModuleEnabledAsync(noTenant, Module));
    }

    [Fact]
    public async Task Missing_projection_does_not_gate()
    {
        // null = proyección aún inexistente (consistencia eventual) → no gatear (evita falso deny).
        var source = new TenantModuleEntitlementsSource(new FakeReader(null));
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

    private sealed class FakeReader(IReadOnlyList<string>? modules) : ITenantEntitlementModulesReader
    {
        public Task<IReadOnlyList<string>?> GetEnabledModulesAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(modules);
    }
}
