using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Domain.Settings;
using TaxVision.Subscription.Infrastructure.Persistence;

namespace TaxVision.Subscription.Tests.Persistence;

/// <summary>
/// RBAC Fase 5 (RBAC_Hardening_Plan.md) — cubre el <c>HasQueryFilter</c> global fail-closed de
/// <see cref="SubscriptionDbContext"/> (safety net EF Core, Layer 3a). Usa
/// <see cref="SubscriptionTenantSettings"/> como aggregate ITenantOwned representativo — el
/// mecanismo genérico aplica igual a <c>TenantSubscription</c>/<c>SubscriptionSeat</c>/
/// <c>TenantAddOn</c>. <c>SubscriptionPlan</c>/<c>AddOnDefinition</c> (catálogo global) y
/// <c>SubscriptionAuditLog</c>/<c>TenantEntitlementSnapshot</c> (TenantId propio sin ITenantOwned)
/// no implementan ITenantOwned, así que el filtro no los alcanza.
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

    private static SubscriptionDbContext CreateContext(string databaseName, FakeTenantContext tenantContext) =>
        new(
            new DbContextOptionsBuilder<SubscriptionDbContext>().UseInMemoryDatabase(databaseName).Options,
            tenantContext
        );

    private static SubscriptionTenantSettings CreateSettings(Guid tenantId) =>
        SubscriptionTenantSettings.Default(tenantId, Guid.NewGuid(), DateTime.UtcNow).Value;

    [Fact]
    public async Task Global_query_filter_isolates_tenant_A_from_tenant_B_data()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var tenantContext = new FakeTenantContext();
        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            await seedDb.TenantSettings.AddRangeAsync(CreateSettings(tenantA), CreateSettings(tenantB));
            await seedDb.SaveChangesAsync();
        }

        tenantContext.SetTenant(tenantA);
        await using var db = CreateContext(databaseName, tenantContext);

        var visible = await db.TenantSettings.ToListAsync();

        var settings = Assert.Single(visible);
        Assert.Equal(tenantA, settings.TenantId);
    }

    [Fact]
    public async Task Global_query_filter_returns_empty_when_TenantContext_has_no_tenant()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantContext = new FakeTenantContext();

        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            await seedDb.TenantSettings.AddAsync(CreateSettings(Guid.NewGuid()));
            await seedDb.SaveChangesAsync();
        }

        // tenantContext nunca se seteó — HasTenant == false, fail-closed compara contra Guid.Empty.
        await using var db = CreateContext(databaseName, tenantContext);

        var visible = await db.TenantSettings.ToListAsync();

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
            await seedDb.TenantSettings.AddRangeAsync(CreateSettings(tenantA), CreateSettings(tenantB));
            await seedDb.SaveChangesAsync();
        }

        tenantContext.SetTenant(tenantA);
        await using var db = CreateContext(databaseName, tenantContext);

        var all = await db.TenantSettings.IgnoreQueryFilters().ToListAsync();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, s => s.TenantId == tenantA);
        Assert.Contains(all, s => s.TenantId == tenantB);
    }
}
