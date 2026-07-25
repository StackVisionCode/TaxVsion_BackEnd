using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Persistence;

namespace TaxVision.CloudStorage.Tests.Persistence;

/// <summary>
/// RBAC Fase 5 (RBAC_Hardening_Plan.md) — cubre el <c>HasQueryFilter</c> global fail-closed de
/// <see cref="CloudStorageDbContext"/> (safety net EF Core, Layer 3a). Usa <see cref="FileObject"/>
/// como aggregate ITenantOwned representativo — el mecanismo genérico aplica igual a cualquier
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

    private static CloudStorageDbContext CreateContext(string databaseName, FakeTenantContext tenantContext) =>
        new(
            new DbContextOptionsBuilder<CloudStorageDbContext>().UseInMemoryDatabase(databaseName).Options,
            tenantContext
        );

    private static FileObject CreateFile(Guid tenantId)
    {
        var key = ObjectKey.Create($"tenants/{tenantId:N}/tenant/documents/2025/{Guid.NewGuid():N}.pdf").Value;
        return FileObject
            .Register(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Tenant,
                null,
                FolderType.Documents,
                2025,
                key,
                "return.pdf",
                "application/pdf",
                10,
                Guid.NewGuid(),
                DateTime.UtcNow,
                DateTime.UtcNow.AddHours(24)
            )
            .Value;
    }

    [Fact]
    public async Task Global_query_filter_isolates_tenant_A_from_tenant_B_data()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var tenantContext = new FakeTenantContext();
        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            await seedDb.Files.AddRangeAsync(CreateFile(tenantA), CreateFile(tenantB));
            await seedDb.SaveChangesAsync();
        }

        tenantContext.SetTenant(tenantA);
        await using var db = CreateContext(databaseName, tenantContext);

        var visible = await db.Files.ToListAsync();

        var file = Assert.Single(visible);
        Assert.Equal(tenantA, file.TenantId);
    }

    [Fact]
    public async Task Global_query_filter_returns_empty_when_TenantContext_has_no_tenant()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantContext = new FakeTenantContext();

        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            await seedDb.Files.AddAsync(CreateFile(Guid.NewGuid()));
            await seedDb.SaveChangesAsync();
        }

        // tenantContext nunca se seteó — HasTenant == false, fail-closed compara contra Guid.Empty.
        await using var db = CreateContext(databaseName, tenantContext);

        var visible = await db.Files.ToListAsync();

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
            await seedDb.Files.AddRangeAsync(CreateFile(tenantA), CreateFile(tenantB));
            await seedDb.SaveChangesAsync();
        }

        tenantContext.SetTenant(tenantA);
        await using var db = CreateContext(databaseName, tenantContext);

        var all = await db.Files.IgnoreQueryFilters().ToListAsync();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, f => f.TenantId == tenantA);
        Assert.Contains(all, f => f.TenantId == tenantB);
    }
}
