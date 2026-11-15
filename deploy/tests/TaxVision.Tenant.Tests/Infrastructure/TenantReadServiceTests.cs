using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tenant.Infrastructure.Persistence;
using TenantEntity = TaxVision.Tenant.Domain.Tenant;

namespace TaxVision.Tenant.Tests.Infrastructure;

public sealed class TenantReadServiceTests
{
    [Fact]
    public async Task GetPageAsync_includes_status_and_version_for_the_directory()
    {
        await using var db = new TenantDbContext(
            new DbContextOptionsBuilder<TenantDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenantContext()
        );
        var active = TenantEntity.Create("Alpha Tax", "alpha-tax", "America/New_York", DateTime.UtcNow).Value;
        var suspended = TenantEntity.Create("Beta Tax", "beta-tax", "UTC", DateTime.UtcNow).Value;
        suspended.Suspend();
        db.Tenants.AddRange(active, suspended);
        await db.SaveChangesAsync();

        var page = await new TenantReadService(db).GetPageAsync(1, 10);

        var alpha = Assert.Single(page, t => t.Id == active.Id);
        Assert.Equal("Active", alpha.Status);
        Assert.True(alpha.IsActive);
        Assert.Equal(active.Version, alpha.Version);
        var beta = Assert.Single(page, t => t.Id == suspended.Id);
        Assert.Equal("Suspended", beta.Status);
        Assert.False(beta.IsActive);
        Assert.Equal(suspended.Version, beta.Version);
    }

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
