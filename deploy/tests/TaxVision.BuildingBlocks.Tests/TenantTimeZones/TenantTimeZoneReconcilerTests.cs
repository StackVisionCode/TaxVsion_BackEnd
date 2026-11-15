using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.TenantTimeZones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.TenantTimeZones;

/// <summary>Reconciliación de la proyección contra el directorio interno de Tenant (TZ13-D2).</summary>
public sealed class TenantTimeZoneReconcilerTests
{
    private static readonly DateTime ReadAt = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid A = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid B = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
    private static readonly Guid C = Guid.Parse("cccccccc-0000-4000-8000-000000000003");

    [Fact]
    public async Task Adds_the_offices_the_service_never_heard_about()
    {
        var (db, store, resolver, uow) = Setup();
        var client = new FakeDirectory([
            [Office(A, "America/Chicago"), Office(B, "Asia/Tokyo")],
        ]);

        var outcome = await TenantTimeZoneReconciler.RunAsync(
            client,
            store,
            resolver,
            uow,
            pageSize: 10,
            ReadAt,
            default
        );

        Assert.True(outcome.TenantReachable);
        Assert.Equal(2, outcome.Applied);
        Assert.Equal("America/Chicago", (await resolver.ResolveAsync(A)).Id);
        Assert.Equal("Asia/Tokyo", (await resolver.ResolveAsync(B)).Id);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Fixes_a_zone_that_missed_its_update_and_leaves_matching_ones_alone()
    {
        var (db, store, resolver, uow) = Setup();
        await store.AddAsync(TenantTimeZoneProjection.Create(A, "Etc/UTC", 1, ReadAt.AddDays(-3)));
        await store.AddAsync(TenantTimeZoneProjection.Create(B, "Asia/Tokyo", 1, ReadAt.AddDays(-3)));
        await uow.SaveChangesAsync();
        Assert.Equal("Etc/UTC", (await resolver.ResolveAsync(A)).Id);
        var savesBefore = uow.Saves;

        var outcome = await TenantTimeZoneReconciler.RunAsync(
            new FakeDirectory([
                [Office(A, "America/New_York"), Office(B, "Asia/Tokyo")],
            ]),
            store,
            resolver,
            uow,
            10,
            ReadAt,
            default
        );

        Assert.Equal(1, outcome.Applied);
        Assert.Equal("America/New_York", (await resolver.ResolveAsync(A)).Id);
        Assert.Equal(savesBefore + 1, uow.Saves);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Nothing_to_fix_writes_nothing()
    {
        var (db, store, resolver, uow) = Setup();
        await store.AddAsync(TenantTimeZoneProjection.Create(A, "America/Chicago", 1, ReadAt.AddDays(-1)));
        await uow.SaveChangesAsync();
        var savesBefore = uow.Saves;

        var outcome = await TenantTimeZoneReconciler.RunAsync(
            new FakeDirectory([
                [Office(A, "America/Chicago")],
            ]),
            store,
            resolver,
            uow,
            10,
            ReadAt,
            default
        );

        Assert.Equal(0, outcome.Applied);
        Assert.Equal(savesBefore, uow.Saves);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Reads_every_page_until_a_short_one()
    {
        var (db, store, resolver, uow) = Setup();
        var client = new FakeDirectory([
            [Office(A, "America/Chicago"), Office(B, "Asia/Tokyo")],
            [Office(C, "Europe/Madrid")],
        ]);

        var outcome = await TenantTimeZoneReconciler.RunAsync(
            client,
            store,
            resolver,
            uow,
            pageSize: 2,
            ReadAt,
            default
        );

        Assert.Equal(3, outcome.Applied);
        Assert.Equal([1, 2], client.PagesRequested);
        Assert.Equal("Europe/Madrid", (await resolver.ResolveAsync(C)).Id);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task An_unreachable_tenant_is_reported_and_keeps_what_was_applied()
    {
        var (db, store, resolver, uow) = Setup();
        var client = new FakeDirectory([
            [Office(A, "America/Chicago"), Office(B, "Asia/Tokyo")],
            null,
        ]);

        var outcome = await TenantTimeZoneReconciler.RunAsync(
            client,
            store,
            resolver,
            uow,
            pageSize: 2,
            ReadAt,
            default
        );

        Assert.False(outcome.TenantReachable);
        Assert.Equal(2, outcome.Applied);
        Assert.Equal("America/Chicago", (await resolver.ResolveAsync(A)).Id);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task An_invalid_zone_from_the_directory_is_skipped()
    {
        var (db, store, resolver, uow) = Setup();

        var outcome = await TenantTimeZoneReconciler.RunAsync(
            new FakeDirectory([
                [Office(A, "Not/AZone")],
            ]),
            store,
            resolver,
            uow,
            10,
            ReadAt,
            default
        );

        Assert.Equal(0, outcome.Applied);
        Assert.True((await resolver.ResolveAsync(A)).IsFallback);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task An_event_older_than_the_reconciliation_does_not_undo_it()
    {
        var (db, store, resolver, uow) = Setup();
        await TenantTimeZoneReconciler.RunAsync(
            new FakeDirectory([
                [Office(A, "America/New_York")],
            ]),
            store,
            resolver,
            uow,
            10,
            ReadAt,
            default
        );

        var stale = new TenantUpdatedIntegrationEvent
        {
            TenantId = A,
            UpdatedTenantId = A,
            Name = "Office",
            DefaultTimeZoneId = "America/Denver",
            UpdatedAtUtc = ReadAt.AddMinutes(-30),
        };
        await TenantTimeZoneProjectionConsumer.Handle(
            stale,
            store,
            resolver,
            uow,
            new Correlation(),
            NullLoggerFactory.Instance,
            default
        );

        Assert.Equal("America/New_York", (await resolver.ResolveAsync(A)).Id);
        await db.DisposeAsync();
    }

    private static TenantDirectoryZone Office(Guid id, string zone) => new(id, zone);

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

    private sealed class FakeDirectory(IReadOnlyList<IReadOnlyList<TenantDirectoryZone>?> pages)
        : ITenantDirectoryZoneClient
    {
        public List<int> PagesRequested { get; } = [];

        public Task<IReadOnlyList<TenantDirectoryZone>?> ListPageAsync(
            int page,
            int size,
            CancellationToken ct = default
        )
        {
            PagesRequested.Add(page);
            return Task.FromResult(page <= pages.Count ? pages[page - 1] : (IReadOnlyList<TenantDirectoryZone>?)[]);
        }
    }

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
