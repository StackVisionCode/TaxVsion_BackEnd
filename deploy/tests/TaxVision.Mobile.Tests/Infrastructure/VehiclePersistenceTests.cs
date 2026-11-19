using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Mobile.Application.Vehicles;
using TaxVision.Mobile.Application.Vehicles.Commands;
using TaxVision.Mobile.Application.Vehicles.Queries;
using TaxVision.Mobile.Domain.Vehicles;
using TaxVision.Mobile.Infrastructure.Persistence;
using TaxVision.Mobile.Infrastructure.Persistence.Repositories;

namespace TaxVision.Mobile.Tests.Infrastructure;

public sealed class VehiclePersistenceTests
{
    [Fact]
    public async Task Crud_is_scoped_to_customer_and_tenant_even_without_ambient_context()
    {
        using var db = Database();
        var repo = new VehicleRepository(db);
        var tenant = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var created = await CreateVehicleHandler.Handle(
            new(tenant, customer, new("ABC", IsDefault: true)),
            repo,
            db,
            TimeProvider.System,
            default
        );
        Assert.True(created.IsSuccess);
        var id = created.Value.Id;
        Assert.Empty(await db.Vehicles.ToListAsync());
        Assert.Null(await repo.FindAsync(Guid.NewGuid(), customer, id, default));
        Assert.Null(await repo.FindAsync(tenant, Guid.NewGuid(), id, default));
        Assert.Null(await repo.FindAsync(Guid.Empty, customer, id, default));
        var hidden = await UpdateVehicleHandler.Handle(
            new(tenant, Guid.NewGuid(), id, new("STOLEN")),
            repo,
            db,
            TimeProvider.System,
            default
        );
        Assert.Equal(VehicleErrors.NotFound, hidden.Error);
        var updated = await UpdateVehicleHandler.Handle(
            new(tenant, customer, id, new("NEW", CurrentMileage: 12.5m)),
            repo,
            db,
            TimeProvider.System,
            default
        );
        Assert.Equal("NEW", updated.Value.Plate);
        var list = await ListVehiclesHandler.Handle(new(tenant, customer), repo, default);
        Assert.Single(list.Value.Items);
        Assert.Equal(12.5m, list.Value.Items[0].CurrentMileage);
        Assert.True(
            (
                await DeleteVehicleHandler.Handle(new(tenant, customer, id), repo, db, new TripRepository(db), default)
            ).IsSuccess
        );
        Assert.Equal(
            VehicleErrors.NotFound,
            (await GetVehicleHandler.Handle(new(tenant, customer, id), repo, default)).Error
        );
    }

    [Fact]
    public async Task Selecting_default_clears_only_the_same_customer_default()
    {
        using var db = Database();
        var repo = new VehicleRepository(db);
        var tenant = Guid.NewGuid();
        var customer = Guid.NewGuid();
        await CreateVehicleHandler.Handle(
            new(tenant, customer, new("A", IsDefault: true)),
            repo,
            db,
            TimeProvider.System,
            default
        );
        await CreateVehicleHandler.Handle(
            new(tenant, Guid.NewGuid(), new("B", IsDefault: true)),
            repo,
            db,
            TimeProvider.System,
            default
        );
        await CreateVehicleHandler.Handle(
            new(tenant, customer, new("C", IsDefault: true)),
            repo,
            db,
            TimeProvider.System,
            default
        );
        var list = await repo.ListAsync(tenant, customer, 1, 100, default);
        Assert.Equal("C", Assert.Single(list.Items, x => x.IsDefault).Details.Plate);
        Assert.Equal(2, await db.Vehicles.IgnoreQueryFilters().CountAsync(x => x.IsDefault));
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public async Task Invalid_pagination_is_rejected(int page, int size)
    {
        using var db = Database();
        var result = await ListVehiclesHandler.Handle(
            new(Guid.NewGuid(), Guid.NewGuid(), page, size),
            new VehicleRepository(db),
            default
        );
        Assert.Equal(VehicleErrors.InvalidPage, result.Error);
    }

    [Fact]
    public async Task Invalid_update_leaves_persisted_vehicle_unchanged()
    {
        using var db = Database();
        var repo = new VehicleRepository(db);
        var t = Guid.NewGuid();
        var c = Guid.NewGuid();
        var created = await CreateVehicleHandler.Handle(new(t, c, new("ABC")), repo, db, TimeProvider.System, default);
        var result = await UpdateVehicleHandler.Handle(
            new(t, c, created.Value.Id, new(" ")),
            repo,
            db,
            TimeProvider.System,
            default
        );
        Assert.True(result.IsFailure);
        Assert.Equal("ABC", (await repo.FindAsync(t, c, created.Value.Id, default))!.Details.Plate);
    }

    private static MobileDbContext Database() =>
        new(
            new DbContextOptionsBuilder<MobileDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenant()
        );

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
