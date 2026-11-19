using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Mobile.Application.Trips;
using TaxVision.Mobile.Application.Trips.Commands;
using TaxVision.Mobile.Application.Trips.Queries;
using TaxVision.Mobile.Application.Vehicles;
using TaxVision.Mobile.Application.Vehicles.Commands;
using TaxVision.Mobile.Domain.Trips;
using TaxVision.Mobile.Infrastructure.Persistence;
using TaxVision.Mobile.Infrastructure.Persistence.Repositories;

namespace TaxVision.Mobile.Tests.Infrastructure;

public sealed class TripHandlerTests
{
    [Fact]
    public async Task Replays_do_not_duplicate_points_or_create_trips_and_payload_mismatch_is_conflict()
    {
        using var db = Db();
        var repo = new TripRepository(db);
        var vehicles = new VehicleRepository(db);
        var t = Guid.NewGuid();
        var c = Guid.NewGuid();
        var vehicle = (
            await CreateVehicleHandler.Handle(new(t, c, new("ABC")), vehicles, db, TimeProvider.System, default)
        ).Value;
        var at = DateTimeOffset.UtcNow.AddMinutes(-10);
        var request = new StartTripInput(Guid.NewGuid(), vehicle.Id, "business", at, new(0, at, 40, -74, 5));
        var first = await StartTripHandler.Handle(new(t, c, request), repo, vehicles, db, TimeProvider.System, default);
        var again = await StartTripHandler.Handle(new(t, c, request), repo, vehicles, db, TimeProvider.System, default);
        Assert.Equal(first.Value.Id, again.Value.Id);
        Assert.True(again.Value.Replayed);
        Assert.Single(await db.Trips.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(
            TripErrors.Replay,
            (
                await StartTripHandler.Handle(
                    new(t, c, request with { Purpose = "personal" }),
                    repo,
                    vehicles,
                    db,
                    TimeProvider.System,
                    default
                )
            ).Error
        );
        var command = new ChangeTripCommand(
            t,
            c,
            first.Value.Id,
            TripAction.Tracking,
            2,
            at.AddMinutes(1),
            [new(1, at.AddMinutes(1), 40.01, -74, 5)]
        );
        Assert.True((await ChangeTripHandler.Handle(command, repo, db, TimeProvider.System, default)).IsSuccess);
        Assert.True((await ChangeTripHandler.Handle(command, repo, db, TimeProvider.System, default)).Value.Replayed);
        Assert.Equal(2, await db.TripPoints.IgnoreQueryFilters().CountAsync());
        Assert.Equal(
            TripErrors.Replay,
            (
                await ChangeTripHandler.Handle(
                    command with
                    {
                        ObservedAt = at.AddMinutes(2),
                    },
                    repo,
                    db,
                    TimeProvider.System,
                    default
                )
            ).Error
        );
        Assert.Equal(
            TripErrors.NotFound,
            (await GetTripHandler.Handle(new(t, Guid.NewGuid(), first.Value.Id), repo, default)).Error
        );
        Assert.Equal(
            TripErrors.NotFound,
            (
                await ChangeTripHandler.Handle(
                    command with
                    {
                        TenantId = Guid.NewGuid(),
                    },
                    repo,
                    db,
                    TimeProvider.System,
                    default
                )
            ).Error
        );
        Assert.Empty(await db.Trips.ToListAsync());
        Assert.Equal(
            TripErrors.VehicleInUse,
            (await DeleteVehicleHandler.Handle(new(t, c, vehicle.Id), vehicles, db, repo, default)).Error
        );
        Assert.True(
            (
                await ChangeTripHandler.Handle(
                    new(t, c, first.Value.Id, TripAction.Complete, 3, at.AddMinutes(2)),
                    repo,
                    db,
                    TimeProvider.System,
                    default
                )
            ).IsSuccess
        );
        var delete = new ChangeTripCommand(t, c, first.Value.Id, TripAction.Delete, 4, at.AddMinutes(3));
        Assert.True((await ChangeTripHandler.Handle(delete, repo, db, TimeProvider.System, default)).IsSuccess);
        Assert.True((await ChangeTripHandler.Handle(delete, repo, db, TimeProvider.System, default)).Value.Replayed);
        Assert.Equal(
            TripErrors.NotFound,
            (await GetTripHandler.Handle(new(t, c, first.Value.Id), repo, default)).Error
        );
        Assert.Empty((await ListTripsHandler.Handle(new(t, c, new()), repo, default)).Value.Items);
    }

    [Fact]
    public async Task Creation_cannot_use_another_customers_vehicle_and_missing_rate_is_explicit()
    {
        using var db = Db();
        var repo = new TripRepository(db);
        var vehicles = new VehicleRepository(db);
        var t = Guid.NewGuid();
        var c = Guid.NewGuid();
        var vehicle = (
            await CreateVehicleHandler.Handle(new(t, c, new("ABC")), vehicles, db, TimeProvider.System, default)
        ).Value;
        var at = DateTimeOffset.UtcNow;
        var input = new StartTripInput(Guid.NewGuid(), vehicle.Id, "business", at, new(0, at, 40, -74, 5));
        Assert.Equal(
            TripErrors.NotFound,
            (
                await StartTripHandler.Handle(
                    new(t, Guid.NewGuid(), input),
                    repo,
                    vehicles,
                    db,
                    TimeProvider.System,
                    default
                )
            ).Error
        );
        var config = await MileageConfigurationHandler.Handle(
            new(t, "business", DateOnly.FromDateTime(at.UtcDateTime)),
            repo,
            default
        );
        Assert.False(config.Value.IsConfigured);
        Assert.Null(config.Value.MileageRate);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 101)]
    public async Task Invalid_pagination_is_rejected(int page, int size)
    {
        using var db = Db();
        var result = await ListTripsHandler.Handle(
            new(Guid.NewGuid(), Guid.NewGuid(), new(page, size)),
            new TripRepository(db),
            default
        );
        Assert.True(result.IsFailure);
    }

    private static MobileDbContext Db() =>
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
