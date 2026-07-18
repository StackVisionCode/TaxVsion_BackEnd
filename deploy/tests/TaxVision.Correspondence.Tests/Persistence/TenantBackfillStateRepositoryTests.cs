using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Domain.Backfill;
using TaxVision.Correspondence.Infrastructure.Persistence;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;

namespace TaxVision.Correspondence.Tests.Persistence;

public sealed class TenantBackfillStateRepositoryTests
{
    private static CorrespondenceDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<CorrespondenceDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );

    [Fact]
    public async Task ListAllTenantIdsAsync_returns_every_backfilled_tenant()
    {
        await using var db = CreateContext();
        var repository = new TenantBackfillStateRepository(db);
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();
        await repository.AddAsync(TenantBackfillState.Create(tenant1));
        await repository.AddAsync(TenantBackfillState.Create(tenant2));
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
