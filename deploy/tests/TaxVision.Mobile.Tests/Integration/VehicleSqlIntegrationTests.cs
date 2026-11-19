using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Mobile.Application.Vehicles;
using TaxVision.Mobile.Application.Vehicles.Commands;
using TaxVision.Mobile.Domain.Vehicles;
using TaxVision.Mobile.Infrastructure.Persistence;
using TaxVision.Mobile.Infrastructure.Persistence.Repositories;

namespace TaxVision.Mobile.Tests.Integration;

public sealed class VehicleSqlIntegrationTests
{
    [Fact]
    public async Task Migration_crud_unique_plate_and_rowversion_work_on_sql_server()
    {
        var t = Guid.NewGuid();
        var c = Guid.NewGuid();
        Guid id;
        await using (var db = Db())
        {
            var repo = new VehicleRepository(db);
            var created = await CreateVehicleHandler.Handle(
                new(t, c, new("SQL-1", IsDefault: true)),
                repo,
                db,
                TimeProvider.System,
                default
            );
            id = created.Value.Id;
            Assert.NotEmpty((await repo.FindAsync(t, c, id, default))!.RowVersion);
        }
        await using (var db = Db())
        {
            var repo = new VehicleRepository(db);
            var duplicate = Vehicle
                .Create(
                    t,
                    c,
                    VehicleDetails.Create("SQL-1", null, null, null, null, null, 0).Value,
                    false,
                    DateTime.UtcNow
                )
                .Value;
            repo.Add(duplicate);
            var ex = await Assert.ThrowsAsync<ConflictException>(() => db.SaveChangesAsync());
            Assert.Equal("Persistence.UniqueConstraint", ex.Code);
        }
        await using var a = Db();
        await using var b = Db();
        var va = await new VehicleRepository(a).FindAsync(t, c, id, default);
        var vb = await new VehicleRepository(b).FindAsync(t, c, id, default);
        va!.Update(va.Details, false, DateTime.UtcNow);
        await a.SaveChangesAsync();
        vb!.Update(vb.Details, false, DateTime.UtcNow);
        Assert.Equal(
            "Persistence.ConcurrencyConflict",
            (await Assert.ThrowsAsync<ConflictException>(() => b.SaveChangesAsync())).Code
        );
        await using var clean = Db();
        var owned = await new VehicleRepository(clean).FindAsync(t, c, id, default);
        Assert.Null(await new VehicleRepository(clean).FindAsync(t, Guid.NewGuid(), id, default));
        clean.Remove(owned!);
        await clean.SaveChangesAsync();
    }

    [Fact]
    public async Task Default_switch_is_atomic_and_unique_in_sql()
    {
        var t = Guid.NewGuid();
        var c = Guid.NewGuid();
        await using var db = Db();
        var repo = new VehicleRepository(db);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await CreateVehicleHandler.Handle(new(t, c, new("A", IsDefault: true)), repo, db, TimeProvider.System, default);
        await CreateVehicleHandler.Handle(new(t, c, new("B", IsDefault: true)), repo, db, TimeProvider.System, default);
        Assert.Equal(
            "B",
            Assert.Single((await repo.ListAsync(t, c, 1, 100, default)).Items, x => x.IsDefault).Details.Plate
        );
        await transaction.RollbackAsync();
        db.ChangeTracker.Clear();
        Assert.Empty((await repo.ListAsync(t, c, 1, 100, default)).Items);
    }

    private static MobileDbContext Db() =>
        new(
            new DbContextOptionsBuilder<MobileDbContext>()
                .UseSqlServer(MobileIntegrationDatabase.ConnectionString)
                .Options,
            new NoTenant()
        );

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
