using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Infrastructure.Persistence;

namespace TaxVision.Postmaster.Tests.Persistence;

/// <summary>
/// RBAC Fase 5 (RBAC_Hardening_Plan.md) — cubre el <c>HasQueryFilter</c> global fail-closed de
/// <see cref="PostmasterDbContext"/> (safety net EF Core, Layer 3a). Usa <see cref="SentMessage"/>
/// como aggregate ITenantOwned representativo — el mecanismo genérico aplica igual a
/// <c>SentMessageRecipient</c>/<c>SentMessageEvent</c>/<c>TenantEmailProvider</c>.
/// <c>SystemEmailProvider</c>/<c>ProviderHealthStatus</c> (cross-tenant por diseño) y
/// <c>EmailIdempotency</c>/<c>SuppressionListEntry</c>/<c>TenantOAuthAccount</c> (TenantId propio
/// pero sin ITenantOwned, repos siempre filtran explícito) no implementan ITenantOwned, así que el
/// filtro no los alcanza.
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

    private static PostmasterDbContext CreateContext(string databaseName, FakeTenantContext tenantContext) =>
        new(
            new DbContextOptionsBuilder<PostmasterDbContext>().UseInMemoryDatabase(databaseName).Options,
            tenantContext
        );

    private static SentMessage CreateMessage(Guid tenantId) =>
        SentMessage
            .Queue(
                tenantId,
                Guid.NewGuid().ToString("N"),
                "Subject",
                "sender@example.com",
                EmailStream.Transactional,
                "system",
                null,
                null,
                null,
                null,
                null,
                DateTime.UtcNow
            )
            .Value;

    [Fact]
    public async Task Global_query_filter_isolates_tenant_A_from_tenant_B_data()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var tenantContext = new FakeTenantContext();
        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            await seedDb.SentMessages.AddRangeAsync(CreateMessage(tenantA), CreateMessage(tenantB));
            await seedDb.SaveChangesAsync();
        }

        tenantContext.SetTenant(tenantA);
        await using var db = CreateContext(databaseName, tenantContext);

        var visible = await db.SentMessages.ToListAsync();

        var message = Assert.Single(visible);
        Assert.Equal(tenantA, message.TenantId);
    }

    [Fact]
    public async Task Global_query_filter_returns_empty_when_TenantContext_has_no_tenant()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantContext = new FakeTenantContext();

        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            await seedDb.SentMessages.AddAsync(CreateMessage(Guid.NewGuid()));
            await seedDb.SaveChangesAsync();
        }

        // tenantContext nunca se seteó — HasTenant == false, fail-closed compara contra Guid.Empty.
        await using var db = CreateContext(databaseName, tenantContext);

        var visible = await db.SentMessages.ToListAsync();

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
            await seedDb.SentMessages.AddRangeAsync(CreateMessage(tenantA), CreateMessage(tenantB));
            await seedDb.SaveChangesAsync();
        }

        tenantContext.SetTenant(tenantA);
        await using var db = CreateContext(databaseName, tenantContext);

        var all = await db.SentMessages.IgnoreQueryFilters().ToListAsync();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, m => m.TenantId == tenantA);
        Assert.Contains(all, m => m.TenantId == tenantB);
    }
}
