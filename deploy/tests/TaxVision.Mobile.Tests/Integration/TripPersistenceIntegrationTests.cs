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
        // Todas las del ensamblado, sin contarlas a mano: cada migración nueva rompía esta prueba.
        Assert.Equal(verify.Database.GetMigrations(), await verify.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await verify.Trips.IgnoreQueryFilters().ToListAsync());
    }

    // Borrado en lote: no carga los puntos y sigue la transacción de quien llama (el handler de Wolverine).
    [Fact]
    public async Task Deleting_the_points_of_a_trip_is_a_bulk_delete_inside_the_callers_transaction()
    {
        var tenant = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var id = await SeedCompletedTripAsync(tenant, customer, DateTime.UtcNow.AddHours(-2), points: 3);
        await using var db = Db(MobileIntegrationDatabase.ConnectionString);
        var trips = new TripRepository(db);

        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await trips.DeletePointsAsync(tenant, customer, id, default);

            Assert.Empty(db.ChangeTracker.Entries<TripPoint>());
            Assert.Equal(0, await db.TripPoints.IgnoreQueryFilters().CountAsync(p => p.TripId == id));
            await transaction.RollbackAsync();
        }
        Assert.Equal(3, await db.TripPoints.IgnoreQueryFilters().CountAsync(p => p.TripId == id));

        // Otro cliente u otra oficina no borran nada.
        await trips.DeletePointsAsync(tenant, Guid.NewGuid(), id, default);
        await trips.DeletePointsAsync(Guid.NewGuid(), customer, id, default);
        await trips.DeletePointsAsync(Guid.Empty, Guid.Empty, id, default);
        await db.SaveChangesAsync();
        Assert.Equal(3, await db.TripPoints.IgnoreQueryFilters().CountAsync(p => p.TripId == id));

        await trips.DeletePointsAsync(tenant, customer, id, default);
        await db.SaveChangesAsync();
        Assert.Equal(0, await db.TripPoints.IgnoreQueryFilters().CountAsync(p => p.TripId == id));
    }

    // SQL Server solo entiende ids de Windows en AT TIME ZONE: uno que no conoce no debe llegar a la consulta.
    [Fact]
    public async Task Daily_mileage_with_a_zone_sql_server_does_not_know_is_grouped_by_its_local_day()
    {
        var tenant = Guid.NewGuid();
        var customer = Guid.NewGuid();
        // 20:00 UTC del día 10 ya es el día 11 en una zona +05:30.
        var start = new DateTime(2026, 3, 10, 20, 0, 0, DateTimeKind.Utc);
        await SeedCompletedTripAsync(tenant, customer, start, points: 1);
        await SeedCompletedTripAsync(tenant, customer, start.AddHours(-10), points: 1);
        var unknownToSql = TimeZoneInfo.CreateCustomTimeZone(
            "Custom/Plus0530",
            new TimeSpan(5, 30, 0),
            "Custom",
            "Custom"
        );
        await using var db = Db(MobileIntegrationDatabase.ConnectionString);

        var days = await new TripRepository(db).DailyAsync(
            tenant,
            customer,
            new DateOnly(2026, 3, 10),
            new DateOnly(2026, 3, 11),
            unknownToSql,
            default
        );

        Assert.Equal([new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 11)], days.Select(d => d.Day));
        Assert.Equal([1, 1], days.Select(d => d.TripsCount));
    }

    // Un id IANA (Linux) y su equivalente de Windows dan el mismo resultado por la consulta de SQL.
    [Theory]
    [InlineData("America/New_York")]
    [InlineData("Eastern Standard Time")]
    public async Task Daily_mileage_accepts_iana_and_windows_zone_ids(string zoneId)
    {
        var tenant = Guid.NewGuid();
        var customer = Guid.NewGuid();
        // 03:00 UTC del día 11 todavía es el día 10 en Nueva York.
        await SeedCompletedTripAsync(tenant, customer, new DateTime(2026, 3, 11, 3, 0, 0, DateTimeKind.Utc), points: 1);
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var zone = TimeZoneInfo.CreateCustomTimeZone(
            zoneId,
            eastern.BaseUtcOffset,
            zoneId,
            zoneId,
            zoneId,
            eastern.GetAdjustmentRules()
        );
        await using var db = Db(MobileIntegrationDatabase.ConnectionString);

        var days = await new TripRepository(db).DailyAsync(
            tenant,
            customer,
            new DateOnly(2026, 3, 10),
            new DateOnly(2026, 3, 11),
            zone,
            default
        );

        Assert.Equal([1, 0], days.Select(d => d.TripsCount));
    }

    private static async Task<Guid> SeedCompletedTripAsync(Guid tenant, Guid customer, DateTime start, int points)
    {
        await using var db = Db(MobileIntegrationDatabase.ConnectionString);
        var vehicle = Vehicle
            .Create(
                tenant,
                customer,
                VehicleDetails.Create("P" + Guid.NewGuid().ToString("N")[..8], null, null, null, null, null, 0).Value,
                false,
                start
            )
            .Value;
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();
        var first = new GpsSample(0, start, 40, -74, 5);
        var trip = Trip.Start(
            tenant,
            customer,
            Guid.NewGuid(),
            vehicle.Id,
            vehicle.Details.Plate,
            "business",
            null,
            start,
            first,
            start
        ).Value;
        var end = start.AddMinutes(30);
        Assert.True(trip.Complete(2, end, [], end).IsSuccess);
        db.Trips.Add(trip);
        db.TripPoints.Add(TripPoint.From(trip, first));
        for (var i = 1; i < points; i++)
            db.TripPoints.Add(TripPoint.From(trip, new GpsSample(i, start.AddMinutes(i), 40 + i * 0.01, -74, 5)));
        await db.SaveChangesAsync();
        return trip.Id;
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
