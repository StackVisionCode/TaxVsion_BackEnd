using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Mobile.Domain.Trips;
using TaxVision.Mobile.Domain.Vehicles;
using TaxVision.Mobile.Infrastructure.Persistence;
using TaxVision.Mobile.Infrastructure.Retention;
using TaxVision.Mobile.Infrastructure.Scheduling;

namespace TaxVision.Mobile.Tests.Integration;

/// <summary>La purga por retención borra el recorrido GPS (los puntos), no solo la última posición del viaje.</summary>
public sealed class MileageGpsRetentionJobIntegrationTests
{
    // Reloj fijo: el job no lee la hora de la máquina.
    private static readonly DateTime Now = new(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Expired_trip_loses_its_gps_points_and_is_marked_purged()
    {
        var tenant = Guid.NewGuid();
        await SeedPolicyAsync(tenant, retentionDays: 30);
        var expired = await SeedCompletedTripAsync(tenant, endedAtUtc: Now.AddDays(-400));

        await RunJobAsync();

        await using var db = Db();
        Assert.Equal(0, await PointsOf(db, expired).CountAsync());
        var trip = await db.Trips.IgnoreQueryFilters().SingleAsync(t => t.Id == expired);
        Assert.NotNull(trip.CoordinatesPurgedAtUtc);
        Assert.Equal(0, trip.LastLatitude);
        Assert.Equal(0, trip.LastLongitude);
    }

    [Fact]
    public async Task Trips_inside_the_retention_window_keep_their_points()
    {
        var tenant = Guid.NewGuid();
        await SeedPolicyAsync(tenant, retentionDays: 30);
        var recent = await SeedCompletedTripAsync(tenant, endedAtUtc: Now.AddDays(-2));

        await RunJobAsync();

        await using var db = Db();
        Assert.Equal(3, await PointsOf(db, recent).CountAsync());
        Assert.Null((await db.Trips.IgnoreQueryFilters().SingleAsync(t => t.Id == recent)).CoordinatesPurgedAtUtc);
    }

    // Sin plazo configurado (sin fila o con el plazo vacío) rige el de tres años: nada queda para siempre.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Office_without_a_configured_period_uses_the_three_year_default(bool hasPolicyRow)
    {
        var tenant = Guid.NewGuid();
        if (hasPolicyRow)
            await SeedPolicyAsync(tenant, retentionDays: null);
        var withinDefault = await SeedCompletedTripAsync(tenant, endedAtUtc: Now.AddDays(-400));
        var pastDefault = await SeedCompletedTripAsync(
            tenant,
            endedAtUtc: Now.AddDays(-MileageRetentionPolicy.DefaultRetentionDays - 30)
        );

        await RunJobAsync();

        await using var db = Db();
        Assert.Equal(3, await PointsOf(db, withinDefault).CountAsync());
        Assert.Equal(0, await PointsOf(db, pastDefault).CountAsync());
        Assert.NotNull(
            (await db.Trips.IgnoreQueryFilters().SingleAsync(t => t.Id == pastDefault)).CoordinatesPurgedAtUtc
        );
    }

    // Viajes que una corrida anterior marcó como purgados pero conservaron sus puntos.
    [Fact]
    public async Task Trip_already_marked_purged_gets_its_leftover_points_removed()
    {
        var tenant = Guid.NewGuid();
        await SeedPolicyAsync(tenant, retentionDays: 30);
        var marked = await SeedCompletedTripAsync(tenant, endedAtUtc: Now.AddDays(-400));
        await using (var db = Db())
        {
            var trip = await db.Trips.IgnoreQueryFilters().SingleAsync(t => t.Id == marked);
            trip.PurgeCoordinates(Now.AddDays(-1));
            await db.SaveChangesAsync();
        }

        await RunJobAsync();

        await using var verify = Db();
        Assert.Equal(0, await PointsOf(verify, marked).CountAsync());
    }

    [Fact]
    public async Task Running_twice_is_harmless_and_does_not_touch_other_offices()
    {
        var tenant = Guid.NewGuid();
        var other = Guid.NewGuid();
        await SeedPolicyAsync(tenant, retentionDays: 30);
        var expired = await SeedCompletedTripAsync(tenant, endedAtUtc: Now.AddDays(-400));
        var foreign = await SeedCompletedTripAsync(other, endedAtUtc: Now.AddDays(-400));

        await RunJobAsync();
        await RunJobAsync();

        await using var db = Db();
        Assert.Equal(0, await PointsOf(db, expired).CountAsync());
        Assert.Equal(3, await PointsOf(db, foreign).CountAsync());
    }

    // Dos réplicas a la vez chocan al marcar el mismo viaje: ninguna aborta y el recorrido queda borrado.
    [Fact]
    public async Task Runners_at_the_same_time_do_not_fail_and_still_purge()
    {
        var tenant = Guid.NewGuid();
        await SeedPolicyAsync(tenant, retentionDays: 30);
        var trips = new List<Guid>();
        for (var i = 0; i < 12; i++)
            trips.Add(await SeedCompletedTripAsync(tenant, endedAtUtc: Now.AddDays(-400 - i)));

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(RunJobAsync)));

        await using var db = Db();
        Assert.Equal(0, await db.TripPoints.IgnoreQueryFilters().CountAsync(p => trips.Contains(p.TripId)));
        Assert.Equal(
            12,
            await db
                .Trips.IgnoreQueryFilters()
                .CountAsync(t => trips.Contains(t.Id) && t.CoordinatesPurgedAtUtc != null)
        );
    }

    [Fact]
    public async Task Without_the_lock_the_pass_purges_nothing()
    {
        var tenant = Guid.NewGuid();
        await SeedPolicyAsync(tenant, retentionDays: 30);
        var expired = await SeedCompletedTripAsync(tenant, endedAtUtc: Now.AddDays(-400));
        var locks = new FakeLocks(granted: false);

        var purged = await WithJobAsync(job => job.RunOnceAsync(default), locks: locks);

        Assert.Null(purged);
        await using var db = Db();
        Assert.Equal(3, await PointsOf(db, expired).CountAsync());
    }

    [Fact]
    public async Task With_the_lock_the_pass_purges_and_releases_it()
    {
        var tenant = Guid.NewGuid();
        await SeedPolicyAsync(tenant, retentionDays: 30);
        var expired = await SeedCompletedTripAsync(tenant, endedAtUtc: Now.AddDays(-400));
        var locks = new FakeLocks(granted: true);
        var options = new MileageGpsRetentionOptions { LockTtlMinutes = 45 };

        var purged = await WithJobAsync(job => job.RunOnceAsync(default), options: options, locks: locks);

        Assert.True(purged >= 1);
        Assert.Equal(TimeSpan.FromMinutes(45), locks.Ttl);
        Assert.True(locks.Released);
        await using var db = Db();
        Assert.Equal(0, await PointsOf(db, expired).CountAsync());
    }

    [Fact]
    public async Task One_pass_drains_every_batch_of_an_office()
    {
        var tenant = Guid.NewGuid();
        await SeedPolicyAsync(tenant, retentionDays: 30);
        var trips = new List<Guid>();
        for (var i = 0; i < 5; i++)
            trips.Add(await SeedCompletedTripAsync(tenant, endedAtUtc: Now.AddDays(-400 - i)));

        await RunJobAsync(options: new MileageGpsRetentionOptions { BatchSize = 2, MaxBatchesPerOffice = 20 });

        await using var db = Db();
        Assert.Equal(5, await PurgedOf(db, trips));
        Assert.Equal(0, await db.TripPoints.IgnoreQueryFilters().CountAsync(p => trips.Contains(p.TripId)));
    }

    // El tope evita que una oficina con mucho atraso acapare la pasada: lo que falta sale en la siguiente.
    [Fact]
    public async Task One_pass_stops_at_the_batch_cap_and_the_next_one_continues()
    {
        var tenant = Guid.NewGuid();
        await SeedPolicyAsync(tenant, retentionDays: 30);
        var trips = new List<Guid>();
        for (var i = 0; i < 5; i++)
            trips.Add(await SeedCompletedTripAsync(tenant, endedAtUtc: Now.AddDays(-400 - i)));
        var options = new MileageGpsRetentionOptions { BatchSize = 2, MaxBatchesPerOffice = 2 };

        await RunJobAsync(options: options);

        await using (var db = Db())
        {
            Assert.Equal(4, await PurgedOf(db, trips));
            // Los más viejos primero: queda el más reciente.
            Assert.Equal(3, await PointsOf(db, trips[0]).CountAsync());
        }

        await RunJobAsync(options: options);

        await using var verify = Db();
        Assert.Equal(5, await PurgedOf(verify, trips));
    }

    [Fact]
    public async Task Cancelled_trip_past_the_period_loses_its_points()
    {
        var tenant = Guid.NewGuid();
        await SeedPolicyAsync(tenant, retentionDays: 30);
        var old = Now.AddDays(-400);
        var cancelled = await SeedTripAsync(
            tenant,
            old.AddMinutes(-30),
            trip => Assert.True(trip.Cancel(2, old, old).IsSuccess)
        );
        var recentEnd = Now.AddDays(-2);
        var recent = await SeedTripAsync(
            tenant,
            recentEnd.AddMinutes(-30),
            trip => Assert.True(trip.Cancel(2, recentEnd, recentEnd).IsSuccess)
        );

        await RunJobAsync();

        await using var db = Db();
        Assert.Equal(0, await PointsOf(db, cancelled).CountAsync());
        Assert.Equal(3, await PointsOf(db, recent).CountAsync());
    }

    // Abandonado: nunca se cerró y el servidor no supo más de él desde hace más que el plazo.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Abandoned_trip_without_recent_activity_loses_its_points(bool paused)
    {
        var tenant = Guid.NewGuid();
        await SeedPolicyAsync(tenant, retentionDays: 30);
        var start = Now.AddDays(-400);
        var abandoned = await SeedTripAsync(
            tenant,
            start,
            trip =>
            {
                if (paused)
                    Assert.True(trip.Pause(2, start.AddMinutes(25), start.AddMinutes(25)).IsSuccess);
            }
        );

        await RunJobAsync();

        await using var db = Db();
        Assert.Equal(0, await PointsOf(db, abandoned).CountAsync());
        var trip = await db.Trips.IgnoreQueryFilters().SingleAsync(t => t.Id == abandoned);
        Assert.NotNull(trip.CoordinatesPurgedAtUtc);
        Assert.Equal(paused ? TripStatus.Paused : TripStatus.InProgress, trip.Status);
    }

    // Empezó hace mucho pero sigue vivo: la referencia es la última actividad, no el inicio.
    [Fact]
    public async Task Trip_in_progress_with_recent_activity_keeps_its_points()
    {
        var tenant = Guid.NewGuid();
        await SeedPolicyAsync(tenant, retentionDays: 30);
        var lastSeen = Now.AddHours(-1);
        var alive = await SeedTripAsync(
            tenant,
            Now.AddDays(-400),
            trip => Assert.True(trip.Synchronize(2, lastSeen, [], lastSeen).IsSuccess)
        );

        await RunJobAsync();

        await using var db = Db();
        Assert.Equal(3, await PointsOf(db, alive).CountAsync());
        Assert.Null((await db.Trips.IgnoreQueryFilters().SingleAsync(t => t.Id == alive)).CoordinatesPurgedAtUtc);
    }

    // Un viaje abandonado ya purgado que vuelve a reportar: sus puntos nuevos son recientes y se conservan.
    [Fact]
    public async Task Purged_trip_that_reports_again_keeps_its_new_points()
    {
        var tenant = Guid.NewGuid();
        await SeedPolicyAsync(tenant, retentionDays: 30);
        var resumed = await SeedTripAsync(tenant, Now.AddDays(-400), _ => { });
        await RunJobAsync();
        await using (var db = Db())
        {
            var trip = await db.Trips.IgnoreQueryFilters().SingleAsync(t => t.Id == resumed);
            Assert.NotNull(trip.CoordinatesPurgedAtUtc);
            var fresh = new GpsSample(1, Now.AddMinutes(-1), 41, -75, 5);
            Assert.True(trip.Synchronize(2, Now.AddMinutes(-1), [fresh], Now).IsSuccess);
            db.TripPoints.Add(TripPoint.From(trip, fresh));
            await db.SaveChangesAsync();
        }

        await RunJobAsync();

        await using var verify = Db();
        Assert.Equal(1, await PointsOf(verify, resumed).CountAsync());
    }

    [Fact]
    public async Task The_cutoff_and_the_purge_date_come_from_the_injected_clock()
    {
        var tenant = Guid.NewGuid();
        await SeedPolicyAsync(tenant, retentionDays: 30);
        var trip = await SeedCompletedTripAsync(tenant, endedAtUtc: Now.AddDays(-10));

        await RunJobAsync(now: Now);

        await using (var db = Db())
            Assert.Equal(3, await PointsOf(db, trip).CountAsync());

        var later = Now.AddDays(25);
        await RunJobAsync(now: later);

        await using var verify = Db();
        Assert.Equal(0, await PointsOf(verify, trip).CountAsync());
        Assert.Equal(
            later,
            (await verify.Trips.IgnoreQueryFilters().SingleAsync(t => t.Id == trip)).CoordinatesPurgedAtUtc
        );
    }

    private static IQueryable<TripPoint> PointsOf(MobileDbContext db, Guid tripId) =>
        db.TripPoints.IgnoreQueryFilters().Where(p => p.TripId == tripId);

    private static Task<int> PurgedOf(MobileDbContext db, List<Guid> trips) =>
        db.Trips.IgnoreQueryFilters().CountAsync(t => trips.Contains(t.Id) && t.CoordinatesPurgedAtUtc != null);

    private static Task RunJobAsync() => RunJobAsync(null, null);

    private static Task RunJobAsync(DateTime? now = null, MileageGpsRetentionOptions? options = null) =>
        WithJobAsync(job => job.PurgeAsync(default), now, options);

    private static async Task<T> WithJobAsync<T>(
        Func<MileageGpsRetentionJob, Task<T>> run,
        DateTime? now = null,
        MileageGpsRetentionOptions? options = null,
        FakeLocks? locks = null
    )
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext, NoTenant>();
        services.AddDbContext<MobileDbContext>(o => o.UseSqlServer(MobileIntegrationDatabase.ConnectionString));
        await using var provider = services.BuildServiceProvider();
        var job = new MileageGpsRetentionJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            locks ?? new FakeLocks(granted: true),
            Options.Create(options ?? new MileageGpsRetentionOptions()),
            new FixedClock(now ?? Now),
            NullLogger<MileageGpsRetentionJob>.Instance
        );

        return await run(job);
    }

    private static async Task SeedPolicyAsync(Guid tenant, int? retentionDays)
    {
        await using var db = Db();
        db.MileageRetentionPolicies.Add(MileageRetentionPolicy.Create(tenant, retentionDays, Now).Value);
        await db.SaveChangesAsync();
    }

    private static Task<Guid> SeedCompletedTripAsync(Guid tenant, DateTime endedAtUtc) =>
        SeedTripAsync(
            tenant,
            endedAtUtc.AddMinutes(-30),
            trip => Assert.True(trip.Complete(2, endedAtUtc, [], endedAtUtc).IsSuccess)
        );

    // Viaje con 3 puntos GPS (el de inicio y dos más); shape lo lleva al estado del caso.
    private static async Task<Guid> SeedTripAsync(Guid tenant, DateTime start, Action<Trip> shape)
    {
        var customer = Guid.NewGuid();
        await using var db = Db();
        var vehicle = Vehicle
            .Create(
                tenant,
                customer,
                VehicleDetails.Create("RETAIN", null, null, null, null, null, 0).Value,
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
        shape(trip);
        db.Trips.Add(trip);
        db.TripPoints.Add(TripPoint.From(trip, first));
        db.TripPoints.Add(TripPoint.From(trip, new GpsSample(1, start.AddMinutes(10), 40.01, -74.01, 5)));
        db.TripPoints.Add(TripPoint.From(trip, new GpsSample(2, start.AddMinutes(20), 40.02, -74.02, 5)));
        await db.SaveChangesAsync();
        return trip.Id;
    }

    private static MobileDbContext Db() =>
        new(
            new DbContextOptionsBuilder<MobileDbContext>()
                .UseSqlServer(MobileIntegrationDatabase.ConnectionString)
                .Options,
            new NoTenant()
        );

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private sealed class FakeLocks(bool granted) : IDistributedLockFactory
    {
        public TimeSpan? Ttl { get; private set; }
        public bool Released { get; private set; }

        public Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default)
        {
            Ttl = ttl;
            return Task.FromResult<IAsyncDisposable?>(granted ? new Handle(this) : null);
        }

        private sealed class Handle(FakeLocks owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner.Released = true;
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
