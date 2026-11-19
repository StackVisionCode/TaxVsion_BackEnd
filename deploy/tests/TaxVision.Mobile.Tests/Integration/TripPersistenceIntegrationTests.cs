extern alias BbTesting;
using BbTesting::TaxVision.BuildingBlocks.Testing.Integration;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using TaxVision.Mobile.Domain.Trips;
using TaxVision.Mobile.Domain.Vehicles;
using TaxVision.Mobile.Infrastructure.Persistence;
using TaxVision.Mobile.Infrastructure.Persistence.Repositories;

namespace TaxVision.Mobile.Tests.Integration;

public sealed class TripPersistenceIntegrationTests
{
    [Fact]
    public async Task Tracking_migration_preserves_existing_vehicles()
    {
        Guid existing = Guid.Empty;
        var database = new TemporaryTestDatabase(
            "mob_upgrade",
            () =>
                new ConfigurationBuilder()
                    .AddUserSecrets("taxvision-mobile-development")
                    .Build()
                    .GetConnectionString("Default")
                ?? throw new InvalidOperationException("Configure sandbox Mobile SQL."),
            connection =>
            {
                using var db = Db(connection);
                db.GetService<IMigrator>().Migrate("20261118202003_InitialCreate");
                var vehicle = Vehicle
                    .Create(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        VehicleDetails.Create("EXISTING", null, null, null, null, null, 0).Value,
                        true,
                        DateTime.UtcNow
                    )
                    .Value;
                db.Vehicles.Add(vehicle);
                db.SaveChanges();
                existing = vehicle.Id;
                db.Database.Migrate();
            }
        );
        await using var verify = Db(database.ConnectionString);
        Assert.Equal(
            "EXISTING",
            (await verify.Vehicles.IgnoreQueryFilters().SingleAsync(x => x.Id == existing)).Details.Plate
        );
        Assert.Equal(2, (await verify.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Empty(await verify.Trips.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Concurrent_changes_to_the_same_trip_conflict_and_snapshot_is_not_overwritten()
    {
        var tenant = Guid.NewGuid();
        var customer = Guid.NewGuid();
        Guid id;
        var start = DateTime.UtcNow.AddMinutes(-10);
        await using (var db = Db(MobileIntegrationDatabase.ConnectionString))
        {
            var vehicle = Vehicle
                .Create(
                    tenant,
                    customer,
                    VehicleDetails.Create("CONCURRENT", null, null, null, null, null, 0).Value,
                    false,
                    start
                )
                .Value;
            db.Vehicles.Add(vehicle);
            await db.SaveChangesAsync();
            var trip = Trip.Start(
                tenant,
                customer,
                Guid.NewGuid(),
                vehicle.Id,
                vehicle.Details.Plate,
                "business",
                null,
                start,
                new(0, start, 40, -74, 5),
                DateTime.UtcNow
            ).Value;
            db.Trips.Add(trip);
            await db.SaveChangesAsync();
            id = trip.Id;
        }
        await using var a = Db(MobileIntegrationDatabase.ConnectionString);
        await using var b = Db(MobileIntegrationDatabase.ConnectionString);
        var left = (await new TripRepository(a).FindAsync(tenant, customer, id, default))!;
        var right = (await new TripRepository(b).FindAsync(tenant, customer, id, default))!;
        Assert.True(left.Edit(2, "business", "left", start.AddMinutes(1), DateTime.UtcNow).IsSuccess);
        Assert.True(right.Edit(2, "business", "right", start.AddMinutes(1), DateTime.UtcNow).IsSuccess);
        await a.SaveChangesAsync();
        Assert.Equal(
            "Persistence.ConcurrencyConflict",
            (await Assert.ThrowsAsync<ConflictException>(() => b.SaveChangesAsync())).Code
        );
        await using var verify = Db(MobileIntegrationDatabase.ConnectionString);
        Assert.Equal("left", (await new TripRepository(verify).FindAsync(tenant, customer, id, default))!.Notes);
    }

    private static MobileDbContext Db(string connection) =>
        new(new DbContextOptionsBuilder<MobileDbContext>().UseSqlServer(connection).Options, new NoTenant());

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
