using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Domain.Backfill;
using TaxVision.Correspondence.Infrastructure.Persistence;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;

namespace TaxVision.Correspondence.Tests.Persistence;

public sealed class TenantBackfillStateRepositoryTests
{
    // NoTenantContext: ambos tests ejercitan exclusivamente ListAllTenantIdsAsync, que usa
    // IgnoreQueryFilters() (job cross-tenant, RBAC Fase 5) — el filtro global fail-closed nunca
    // entra en juego acá.
    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    private static CorrespondenceDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<CorrespondenceDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new NoTenantContext()
        );

    private static TenantBackfillState Completed(Guid tenantId)
    {
        var state = TenantBackfillState.Start(tenantId);
        state.ConfirmPage(1, isLastPage: true);
        return state;
    }

    [Fact]
    public async Task ListAllTenantIdsAsync_returns_every_backfilled_tenant()
    {
        await using var db = CreateContext();
        var repository = new TenantBackfillStateRepository(db);
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();
        await repository.AddAsync(Completed(tenant1));
        await repository.AddAsync(Completed(tenant2));
        await repository.AddAsync(TenantBackfillState.Start(Guid.NewGuid()));
        await db.SaveChangesAsync();

        var tenantIds = await repository.ListAllTenantIdsAsync();

        Assert.Equal(2, tenantIds.Count);
        Assert.Contains(tenant1, tenantIds);
        Assert.Contains(tenant2, tenantIds);
    }

    [Fact]
    public async Task ListAllTenantIdsAsync_returns_empty_when_no_tenant_was_backfilled_yet()
    {
        await using var db = CreateContext();
        var repository = new TenantBackfillStateRepository(db);

        var tenantIds = await repository.ListAllTenantIdsAsync();

        Assert.Empty(tenantIds);
    }
}
