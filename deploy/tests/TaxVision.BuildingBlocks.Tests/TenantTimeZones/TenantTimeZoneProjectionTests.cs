using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.TenantTimeZones;
using BuildingBlocks.TimeZones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.TenantTimeZones;

/// <summary>Proyección opcional de la zona de la oficina: consumer ordenado por versión, store EF y resolver con caché.</summary>
public sealed class TenantTimeZoneProjectionTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-2222-4333-8444-555555555555");
    private static readonly DateTime T0 = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task TenantCreated_projects_the_office_zone()
    {
        var (db, store, resolver, uow) = Setup();

        await TenantTimeZoneProjectionConsumer.Handle(
            Created("America/Chicago"),
            store,
            resolver,
            uow,
            new Correlation(),
            NullLoggerFactory.Instance,
            default
        );

        Assert.Equal("America/Chicago", (await resolver.ResolveAsync(TenantId)).Id);
        Assert.Equal(1, uow.Saves);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task TenantUpdated_replaces_the_zone_and_the_cache()
    {
        var (db, store, resolver, uow) = Setup();
        await TenantTimeZoneProjectionConsumer.Handle(
            Created("America/Chicago"),
            store,
            resolver,
            uow,
            new Correlation(),
            NullLoggerFactory.Instance,
            default
        );
        Assert.Equal("America/Chicago", (await resolver.ResolveAsync(TenantId)).Id);

        await TenantTimeZoneProjectionConsumer.Handle(
            Updated("America/New_York", version: 2, at: T0.AddMinutes(5)),
            store,
            resolver,
            uow,
            new Correlation(),
            NullLoggerFactory.Instance,
            default
        );

        Assert.Equal("America/New_York", (await resolver.ResolveAsync(TenantId)).Id);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task An_older_update_never_overwrites_a_newer_one()
    {
        var (db, store, resolver, uow) = Setup();
        await TenantTimeZoneProjectionConsumer.Handle(
            Updated("America/New_York", version: 3, at: T0.AddMinutes(10)),
            store,
            resolver,
            uow,
            new Correlation(),
            NullLoggerFactory.Instance,
            default
        );

        await TenantTimeZoneProjectionConsumer.Handle(
            Updated("America/Denver", version: 2, at: T0.AddMinutes(20)),
            store,
            resolver,
            uow,
            new Correlation(),
            NullLoggerFactory.Instance,
            default
        );
        await TenantTimeZoneProjectionConsumer.Handle(
            Created("America/Chicago"),
            store,
            resolver,
            uow,
            new Correlation(),
            NullLoggerFactory.Instance,
            default
        );

        Assert.Equal("America/New_York", (await resolver.ResolveAsync(TenantId)).Id);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Without_versions_the_update_time_orders_the_events()
    {
        var (db, store, resolver, uow) = Setup();
        await TenantTimeZoneProjectionConsumer.Handle(
            Updated("America/New_York", version: null, at: T0.AddMinutes(10)),
            store,
            resolver,
            uow,
            new Correlation(),
            NullLoggerFactory.Instance,
            default
        );

        await TenantTimeZoneProjectionConsumer.Handle(
            Updated("America/Denver", version: null, at: T0.AddMinutes(5)),
            store,
            resolver,
            uow,
            new Correlation(),
            NullLoggerFactory.Instance,
            default
        );

        Assert.Equal("America/New_York", (await resolver.ResolveAsync(TenantId)).Id);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task An_invalid_zone_is_not_projected()
    {
        var (db, store, resolver, uow) = Setup();
        await TenantTimeZoneProjectionConsumer.Handle(
            Created("America/Chicago"),
            store,
            resolver,
            uow,
            new Correlation(),
            NullLoggerFactory.Instance,
            default
        );

        await TenantTimeZoneProjectionConsumer.Handle(
            Updated("Not/AZone", version: 2, at: T0.AddMinutes(5)),
            store,
            resolver,
            uow,
            new Correlation(),
            NullLoggerFactory.Instance,
            default
        );

        Assert.Equal("America/Chicago", (await resolver.ResolveAsync(TenantId)).Id);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task A_tenant_without_projection_falls_back_to_utc_and_says_so()
    {
        var (db, _, resolver, _) = Setup();

        var zone = await resolver.ResolveAsync(TenantId);

        Assert.Equal(IanaTimeZone.UtcId, zone.Id);
        Assert.True(zone.IsFallback);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Each_tenant_keeps_its_own_zone()
    {
        var (db, store, resolver, uow) = Setup();
        var other = Guid.NewGuid();
        await TenantTimeZoneProjectionConsumer.Handle(
            Created("America/Chicago"),
            store,
            resolver,
            uow,
            new Correlation(),
            NullLoggerFactory.Instance,
            default
        );
        await TenantTimeZoneProjectionConsumer.Handle(
            Created("Asia/Tokyo") with
            {
                NewTenantId = other,
                TenantId = other,
            },
            store,
            resolver,
            uow,
            new Correlation(),
            NullLoggerFactory.Instance,
            default
        );

        Assert.Equal("America/Chicago", (await resolver.ResolveAsync(TenantId)).Id);
        Assert.Equal("Asia/Tokyo", (await resolver.ResolveAsync(other)).Id);
        await db.DisposeAsync();
    }

    private static (
        TestDbContext Db,
        TenantTimeZoneStore Store,
        TenantTimeZoneResolver Resolver,
        CountingUnitOfWork Uow
    ) Setup()
    {
        var db = new TestDbContext(
            new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options
        );
        var store = new TenantTimeZoneStore(db);
        var resolver = new TenantTimeZoneResolver(
            store,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<TenantTimeZoneResolver>.Instance
        );
        return (db, store, resolver, new CountingUnitOfWork(db));
    }

    private static TenantCreatedIntegrationEvent Created(string zone) =>
        new()
        {
            TenantId = TenantId,
            NewTenantId = TenantId,
            Name = "Castillo Tax",
            SubDomain = "castillo",
            DefaultTimeZoneId = zone,
            AdminEmail = "admin@example.test",
            AdminInvitationTokenHash = "hash",
            OccurredOn = T0,
        };

    private static TenantUpdatedIntegrationEvent Updated(string zone, long? version, DateTime at) =>
        new()
        {
            TenantId = TenantId,
            UpdatedTenantId = TenantId,
            Name = "Castillo Tax",
            DefaultTimeZoneId = zone,
            UpdatedAtUtc = at,
            TenantVersion = version,
        };

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.ApplyTenantTimeZoneProjection();
    }

    private sealed class CountingUnitOfWork(DbContext db) : IUnitOfWork
    {
        public int Saves { get; private set; }

        public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            Saves++;
            return await db.SaveChangesAsync(ct);
        }
    }

    private sealed class Correlation : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = string.Empty;

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            CorrelationId = correlationId;
            return new Noop();
        }

        private sealed class Noop : IDisposable
        {
            public void Dispose() { }
        }
    }
}
