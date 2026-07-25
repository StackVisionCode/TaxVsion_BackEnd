using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Persistence;

/// <summary>
/// RBAC Fase 5 (RBAC_Hardening_Plan.md) — cubre el <c>HasQueryFilter</c> global fail-closed de
/// <see cref="AuthDbContext"/> (safety net EF Core, Layer 3a). Usa <see cref="Role"/> como
/// aggregate ITenantOwned representativo — el mecanismo genérico aplica igual a cualquier
/// entidad que herede <c>TenantEntity</c>.
/// </summary>
public sealed class TenantIsolationTests
{
    private sealed class FakeTenantContext : ITenantContext
    {
        private Guid? _tenantId;
        public Guid TenantId => _tenantId ?? throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => _tenantId.HasValue;

        public void SetTenant(Guid tenantId) => _tenantId = tenantId;
    }

    private static AuthDbContext CreateContext(string databaseName, FakeTenantContext tenantContext) =>
        new(
            new DbContextOptionsBuilder<AuthDbContext>().UseInMemoryDatabase(databaseName).Options,
            new FakeMessageBus(),
            tenantContext
        );

    [Fact]
    public async Task Global_query_filter_isolates_tenant_A_from_tenant_B_data()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var tenantContext = new FakeTenantContext();
        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            var roleA = Role.Create(tenantA, "Role A", null, isSystem: false).Value;
            var roleB = Role.Create(tenantB, "Role B", null, isSystem: false).Value;
            await seedDb.Roles.AddRangeAsync(roleA, roleB);
            await seedDb.SaveChangesAsync();
        }

        tenantContext.SetTenant(tenantA);
        await using var db = CreateContext(databaseName, tenantContext);

        var visible = await db.Roles.ToListAsync();

        var role = Assert.Single(visible);
        Assert.Equal(tenantA, role.TenantId);
    }

    [Fact]
    public async Task Global_query_filter_returns_empty_when_TenantContext_has_no_tenant()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantContext = new FakeTenantContext();

        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            var role = Role.Create(Guid.NewGuid(), "Some Role", null, isSystem: false).Value;
            await seedDb.Roles.AddAsync(role);
            await seedDb.SaveChangesAsync();
        }

        // tenantContext nunca se seteó — HasTenant == false, fail-closed compara contra Guid.Empty.
        await using var db = CreateContext(databaseName, tenantContext);

        var visible = await db.Roles.ToListAsync();

        Assert.Empty(visible);
    }

    [Fact]
    public async Task IgnoreQueryFilters_returns_all_tenants_data()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();

        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            var roleA = Role.Create(tenantA, "Role A", null, isSystem: false).Value;
            var roleB = Role.Create(tenantB, "Role B", null, isSystem: false).Value;
            await seedDb.Roles.AddRangeAsync(roleA, roleB);
            await seedDb.SaveChangesAsync();
        }

        tenantContext.SetTenant(tenantA);
        await using var db = CreateContext(databaseName, tenantContext);

        var all = await db.Roles.IgnoreQueryFilters().ToListAsync();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, r => r.TenantId == tenantA);
        Assert.Contains(all, r => r.TenantId == tenantB);
    }
}
