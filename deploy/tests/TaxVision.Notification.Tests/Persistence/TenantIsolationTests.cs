using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Infrastructure.Persistence;

namespace TaxVision.Notification.Tests.Persistence;

/// <summary>
/// RBAC Fase 5 (RBAC_Hardening_Plan.md) — cubre el <c>HasQueryFilter</c> global fail-closed de
/// <see cref="NotificationDbContext"/> (safety net EF Core, Layer 3a). Usa <see cref="PushDeviceToken"/>
/// como aggregate ITenantOwned representativo — el mecanismo genérico aplica igual a cualquier
/// entidad que herede <c>TenantEntity</c>. EmailProviderConfiguration/EmailTemplate/EmailLayout
/// (System vs Tenant scope con TenantId nullable) y los hijos sin columna propia no implementan
/// ITenantOwned, así que el filtro no los alcanza.
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

    private static NotificationDbContext CreateContext(string databaseName, FakeTenantContext tenantContext) =>
        new(
            new DbContextOptionsBuilder<NotificationDbContext>().UseInMemoryDatabase(databaseName).Options,
            tenantContext
        );

    private static PushDeviceToken CreateToken(Guid tenantId) =>
        PushDeviceToken.Register(tenantId, Guid.NewGuid(), PushPlatform.Fcm, Guid.NewGuid().ToString("N"), null).Value;

    [Fact]
    public async Task Global_query_filter_isolates_tenant_A_from_tenant_B_data()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var tenantContext = new FakeTenantContext();
        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            await seedDb.PushDeviceTokens.AddRangeAsync(CreateToken(tenantA), CreateToken(tenantB));
            await seedDb.SaveChangesAsync();
        }

        tenantContext.SetTenant(tenantA);
        await using var db = CreateContext(databaseName, tenantContext);

        var visible = await db.PushDeviceTokens.ToListAsync();

        var token = Assert.Single(visible);
        Assert.Equal(tenantA, token.TenantId);
    }

    [Fact]
    public async Task Global_query_filter_returns_empty_when_TenantContext_has_no_tenant()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantContext = new FakeTenantContext();

        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            await seedDb.PushDeviceTokens.AddAsync(CreateToken(Guid.NewGuid()));
            await seedDb.SaveChangesAsync();
        }

        // tenantContext nunca se seteó — HasTenant == false, fail-closed compara contra Guid.Empty.
        await using var db = CreateContext(databaseName, tenantContext);

        var visible = await db.PushDeviceTokens.ToListAsync();

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
            await seedDb.PushDeviceTokens.AddRangeAsync(CreateToken(tenantA), CreateToken(tenantB));
            await seedDb.SaveChangesAsync();
        }

        tenantContext.SetTenant(tenantA);
        await using var db = CreateContext(databaseName, tenantContext);

        var all = await db.PushDeviceTokens.IgnoreQueryFilters().ToListAsync();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, t => t.TenantId == tenantA);
        Assert.Contains(all, t => t.TenantId == tenantB);
    }
}
