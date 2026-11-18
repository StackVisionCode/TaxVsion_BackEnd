using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.Customer.Tests.Integration;

/// <summary>
/// F6 en la base migrada: las mal escritas siguen existiendo pero inactivas, y el catálogo activo cubre las
/// ocupaciones de EE. UU. y las situaciones comunes.
/// </summary>
public sealed class OccupationCatalogIntegrationTests
{
    [Fact]
    public async Task The_migrated_catalog_hides_the_misspelled_and_offers_the_new_ones()
    {
        await using var db = new CustomerDbContext(
            new DbContextOptionsBuilder<CustomerDbContext>()
                .UseSqlServer(CustomerIntegrationDatabase.ConnectionString)
                .Options,
            new NoTenant()
        );

        var archtect = await db.Occupations.AsNoTracking().SingleAsync(o => o.Name == "Archtect");
        Assert.False(archtect.IsActive);
        Assert.True(await db.Occupations.AsNoTracking().AnyAsync(o => o.Name == "Retired" && o.IsActive));
        Assert.True(
            await db.Occupations.AsNoTracking().AnyAsync(o => o.Name == "Licensed Practical Nurse" && o.IsActive)
        );
        Assert.True(await db.Occupations.AsNoTracking().CountAsync(o => o.IsActive) >= 1_100);
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
