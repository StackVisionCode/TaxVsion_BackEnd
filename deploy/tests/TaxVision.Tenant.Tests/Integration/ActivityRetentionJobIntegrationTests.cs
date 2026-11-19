using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Tenant.Domain.Team;
using TaxVision.Tenant.Infrastructure.Persistence;
using TaxVision.Tenant.Infrastructure.Scheduling;
using TaxVision.Tenant.Infrastructure.Team;

namespace TaxVision.Tenant.Tests.Integration;

/// <summary>
/// Purga diaria contra una base propia (las filas de otras clases no se ven): borra por lotes lo anterior a 13 meses
/// de cualquier oficina y deja lo demás, incluido lo que cae justo en el corte.
/// </summary>
public sealed class ActivityRetentionJobIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 11, 18, 3, 0, 0, DateTimeKind.Utc);

    private readonly TenantTemporaryDatabase _database = new();
    private ServiceProvider _services = default!;

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        _services = new ServiceCollection()
            .AddSingleton<ITenantContext, NoTenantContext>()
            .AddDbContext<TenantDbContext>(o => o.UseSqlServer(_database.ConnectionString))
            .AddScoped<ActivityRetentionPurger>()
            .BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _database.DropAsync();
    }

    [Fact]
    public async Task A_sweep_deletes_in_batches_everything_older_than_thirteen_months_and_keeps_the_rest()
    {
        var cutoff = ActivityRetention.CutoffFor(Now);
        for (var i = 0; i < 5; i++)
            await SeedAsync(Guid.NewGuid(), cutoff.AddDays(-1 - i));
        var atCutoff = await SeedAsync(Guid.NewGuid(), cutoff);
        var recent = await SeedAsync(Guid.NewGuid(), Now.AddDays(-3));

        var deleted = await Job(batchSize: 2).SweepAsync(Now, CancellationToken.None);

        Assert.Equal(5, deleted);
        await using var db = _database.NewContext();
        var left = await db.ActivityEntries.IgnoreQueryFilters().Select(e => e.Id).ToListAsync();
        Assert.Equal(new[] { atCutoff, recent }.Order(), left.Order());
    }

    [Fact]
    public async Task A_sweep_with_nothing_to_purge_deletes_nothing()
    {
        await SeedAsync(Guid.NewGuid(), Now.AddMonths(-12));

        Assert.Equal(0, await Job(batchSize: 10).SweepAsync(Now, CancellationToken.None));
    }

    private ActivityRetentionJob Job(int batchSize) =>
        new(
            _services.GetRequiredService<IServiceScopeFactory>(),
            new NoLocks(),
            Options.Create(new ActivityRetentionOptions { BatchSize = batchSize }),
            NullLogger<ActivityRetentionJob>.Instance
        );

    private async Task<Guid> SeedAsync(Guid tenantId, DateTime occurredAt)
    {
        var entry = ActivityEntry
            .Create(
                new ActivityEntryDraft(
                    tenantId,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    ActivityVerbs.TaskCreated,
                    Guid.NewGuid(),
                    null,
                    ActivityVisibility.Team,
                    occurredAt
                )
            )
            .Value;
        await using var db = _database.NewContext();
        db.ActivityEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    private sealed class NoLocks : IDistributedLockFactory
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
            Task.FromResult<IAsyncDisposable?>(null);
    }

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
