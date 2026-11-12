using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Subscription.Application.Catalog.Revisions;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Infrastructure.Catalog;
using TaxVision.Subscription.Infrastructure.Configuration;
using TaxVision.Subscription.Infrastructure.Persistence;
using TaxVision.Subscription.Infrastructure.Scheduling;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>
/// El runner de revisiones contra SQL Server, en una base propia: cada revisión corre una sola vez, en su
/// transacción, y una que falla no deja nada y frena el arranque.
/// </summary>
public sealed class CatalogRevisionRunnerIntegrationTests : IAsyncLifetime
{
    private const string Prefix = "subrev_";

    private readonly string _connectionString = new SqlConnectionStringBuilder(SubscriptionIntegrationDb.Connection)
    {
        InitialCatalog = $"{Prefix}{Guid.NewGuid():N}"[..(Prefix.Length + 12)],
    }.ConnectionString;

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
        db.Plans.Add(AddOnTestCatalog.PlanWith("revplan", ["documents"]));
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task A_revision_runs_once_and_is_recorded()
    {
        var revision = new RankRevision("test-rank-once", rank: 7);

        await Runner().RunAsync([revision], CancellationToken.None);
        await Runner().RunAsync([revision], CancellationToken.None);

        Assert.Equal(1, revision.Calls);
        await using var db = NewContext();
        Assert.Equal(7, (await db.Plans.AsNoTracking().SingleAsync()).SalesRank.Value);
        var row = await db.CatalogRevisions.AsNoTracking().SingleAsync();
        Assert.Equal("test-rank-once", row.RevisionId);
        Assert.Equal("rank 7", row.Summary);
        Assert.Null(row.FollowUpPublishedAtUtc);
        var audit = await db.CatalogAuditEntries.AsNoTracking().SingleAsync();
        Assert.Equal("catalog.revision_applied", audit.Action);
        Assert.Equal("deploy:test-rank-once", audit.ActorLabel);
        Assert.Equal("test-rank-once", audit.EntityCode);
        Assert.Contains("rank 7", audit.AfterJson);
    }

    [Fact]
    public async Task A_failing_revision_leaves_nothing_and_stops_the_start()
    {
        var failing = new RankRevision("test-rank-fails", rank: 9, fail: true);

        var error = await Assert.ThrowsAsync<CatalogRevisionFailedException>(() =>
            Runner().RunAsync([failing], CancellationToken.None)
        );

        Assert.Contains("test-rank-fails", error.Message);
        Assert.Contains("Test.Boom", error.Message);
        await using var db = NewContext();
        Assert.Equal(SalesRank.Default.Value, (await db.Plans.AsNoTracking().SingleAsync()).SalesRank.Value);
        Assert.Empty(await db.CatalogRevisions.AsNoTracking().ToListAsync());
        Assert.Empty(await db.CatalogAuditEntries.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Revisions_after_a_failing_one_do_not_run()
    {
        var failing = new RankRevision("test-first-fails", rank: 9, fail: true);
        var next = new RankRevision("test-never-runs", rank: 5);

        await Assert.ThrowsAsync<CatalogRevisionFailedException>(() =>
            Runner().RunAsync([failing, next], CancellationToken.None)
        );

        Assert.Equal(0, next.Calls);
    }

    private CatalogRevisionRunner Runner()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext, NoTenant>();
        services.AddDbContext<SubscriptionDbContext>(options => options.UseSqlServer(_connectionString));
        services.AddSubscriptionCatalogRevisions();
        services.AddSingleton<IDistributedLockFactory, FakeDistributedLockFactory>();
        services.Configure<SubscriptionOptions>(options => options.DefaultPlanCode = "enterprise");
        services.Configure<SubscriptionCatalogOptions>(_ => { });
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<CatalogRevisionRunner>();
    }

    private SubscriptionDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<SubscriptionDbContext>().UseSqlServer(_connectionString).Options,
            new NoTenant()
        );

    /// <summary>Cambia el rango del único plan; si <paramref name="fail"/>, lo cambia y después falla.</summary>
    private sealed class RankRevision(string id, int rank, bool fail = false) : ICatalogRevision
    {
        public int Calls { get; private set; }
        public string Id => id;
        public string Description => "test";

        public async Task<Result<string>> ApplyAsync(CatalogRevisionContext context, CancellationToken ct)
        {
            Calls++;
            var listed = Assert.Single(await context.Plans.GetAllAsync(ct));
            var plan = (await context.Plans.GetByIdForUpdateAsync(listed.Id, ct))!;
            plan.SetSalesRank(SalesRank.Create(rank).Value, Guid.Empty, context.NowUtc);
            return fail ? Result.Failure<string>(new Error("Test.Boom", "boom")) : Result.Success($"rank {rank}");
        }
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
