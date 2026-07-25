using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Domain.Projections;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Infrastructure.Persistence;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;

namespace TaxVision.Correspondence.Tests.Persistence;

public sealed class CustomerEmailAddressRepositoryTests
{
    // RBAC Fase 5 — CustomerEmailAddress ahora es ITenantOwned, así que el HasQueryFilter global
    // entra en juego para estos repos (a diferencia de DraftRepositoryTests/
    // TenantBackfillStateRepositoryTests, que solo ejercitan rutas IgnoreQueryFilters). Se setea
    // al tenant "propio" de cada test antes de consultar, igual que haría
    // JwtTenantContextMiddleware en producción.
    private sealed class FakeTenantContext : ITenantContext
    {
        private Guid? _tenantId;
        public Guid TenantId => _tenantId ?? throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => _tenantId.HasValue;

        public void SetTenant(Guid tenantId) => _tenantId = tenantId;
    }

    private static CorrespondenceDbContext CreateContext(ITenantContext tenantContext) =>
        new(
            new DbContextOptionsBuilder<CorrespondenceDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenantContext
        );

    [Fact]
    public async Task FindActiveByAddressAsync_ignores_soft_deleted_rows()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(tenantId);
        await using var db = CreateContext(tenantContext);
        var repository = new CustomerEmailAddressRepository(db);

        var deleted = CustomerEmailAddress.Create(
            tenantId,
            Guid.NewGuid(),
            EmailAddress.Create("gone@example.com").Value
        );
        deleted.SoftDelete();
        await repository.AddAsync(deleted);
        await db.SaveChangesAsync();

        var found = await repository.FindActiveByAddressAsync(tenantId, "gone@example.com");

        Assert.Null(found);
    }

    [Fact]
    public async Task FindActiveByAddressAsync_returns_the_active_row_scoped_by_tenant()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(tenantId);
        await using var db = CreateContext(tenantContext);
        var repository = new CustomerEmailAddressRepository(db);

        await repository.AddAsync(
            CustomerEmailAddress.Create(tenantId, Guid.NewGuid(), EmailAddress.Create("shared@example.com").Value)
        );
        await repository.AddAsync(
            CustomerEmailAddress.Create(otherTenantId, Guid.NewGuid(), EmailAddress.Create("shared@example.com").Value)
        );
        await db.SaveChangesAsync();

        var found = await repository.FindActiveByAddressAsync(tenantId, "shared@example.com");

        Assert.NotNull(found);
        Assert.Equal(tenantId, found!.TenantId);
    }

    [Fact]
    public async Task GetByCustomerIdAsync_returns_null_when_not_found()
    {
        await using var db = CreateContext(new FakeTenantContext());
        var repository = new CustomerEmailAddressRepository(db);

        var found = await repository.GetByCustomerIdAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(found);
    }
}
