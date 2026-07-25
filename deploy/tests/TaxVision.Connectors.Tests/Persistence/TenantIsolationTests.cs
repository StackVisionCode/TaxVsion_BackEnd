using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.Persistence;

namespace TaxVision.Connectors.Tests.Persistence;

/// <summary>
/// RBAC Fase 5 (RBAC_Hardening_Plan.md) — cubre el <c>HasQueryFilter</c> global fail-closed de
/// <see cref="ConnectorsDbContext"/> (safety net EF Core, Layer 3a). Usa <see cref="TenantEmailAccount"/>
/// como único aggregate ITenantOwned del servicio — las otras 7 entidades (OAuthConnection,
/// OAuthToken, ImapCredentials, SmtpCredentials, ProviderWatchSubscription, ProviderSyncCursor,
/// ProviderConnectionAuditLog) cuelgan de ella por AccountId sin columna TenantId propia, así que
/// el filtro genérico no las alcanza.
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

    private static ConnectorsDbContext CreateContext(string databaseName, FakeTenantContext tenantContext) =>
        new(
            new DbContextOptionsBuilder<ConnectorsDbContext>().UseInMemoryDatabase(databaseName).Options,
            tenantContext
        );

    private static TenantEmailAccount CreateAccount(Guid tenantId) =>
        TenantEmailAccount
            .Create(
                tenantId,
                $"user-{Guid.NewGuid():N}@example.com",
                ProviderCode.Gmail,
                Guid.NewGuid(),
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
            await seedDb.TenantEmailAccounts.AddRangeAsync(CreateAccount(tenantA), CreateAccount(tenantB));
            await seedDb.SaveChangesAsync();
        }

        tenantContext.SetTenant(tenantA);
        await using var db = CreateContext(databaseName, tenantContext);

        var visible = await db.TenantEmailAccounts.ToListAsync();

        var account = Assert.Single(visible);
        Assert.Equal(tenantA, account.TenantId);
    }

    [Fact]
    public async Task Global_query_filter_returns_empty_when_TenantContext_has_no_tenant()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantContext = new FakeTenantContext();

        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            await seedDb.TenantEmailAccounts.AddAsync(CreateAccount(Guid.NewGuid()));
            await seedDb.SaveChangesAsync();
        }

        // tenantContext nunca se seteó — HasTenant == false, fail-closed compara contra Guid.Empty.
        await using var db = CreateContext(databaseName, tenantContext);

        var visible = await db.TenantEmailAccounts.ToListAsync();

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
            await seedDb.TenantEmailAccounts.AddRangeAsync(CreateAccount(tenantA), CreateAccount(tenantB));
            await seedDb.SaveChangesAsync();
        }

        tenantContext.SetTenant(tenantA);
        await using var db = CreateContext(databaseName, tenantContext);

        var all = await db.TenantEmailAccounts.IgnoreQueryFilters().ToListAsync();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, a => a.TenantId == tenantA);
        Assert.Contains(all, a => a.TenantId == tenantB);
    }
}
