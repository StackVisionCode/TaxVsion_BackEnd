using BuildingBlocks.Caching;
using BuildingBlocks.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Infrastructure.Persistence.Repositories;
using TaxVision.Subscription.Infrastructure.RateLimiting;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>D12 contra SQL Server: la migración siembra el perfil de Enterprise y un plan sin filas lo usa.</summary>
public sealed class RateLimitDefaultProfileIntegrationTests
{
    [Fact]
    public async Task The_migration_seeds_the_enterprise_profile()
    {
        await using var db = SubscriptionIntegrationDb.NewContext(Guid.Empty);

        var profile = await new RateLimitDefaultProfileRepository(db).GetAsync();

        Assert.NotNull(profile);
        Assert.Equal(10m, profile!.MultiplierFor(RateLimitCategory.F).Value);
        Assert.Equal(20m, profile.MultiplierFor(RateLimitCategory.K).Value);
        Assert.Equal(1m, profile.MultiplierFor(RateLimitCategory.M).Value);
    }

    [Fact]
    public async Task A_plan_without_rows_reads_the_profile_instead_of_the_base_quota()
    {
        var plan = AddOnTestCatalog.PlanWith($"zrl{Guid.NewGuid():N}"[..12], ["documents"]);
        plan.ClearDomainEvents();
        await using (var seed = SubscriptionIntegrationDb.NewContext(Guid.Empty))
        {
            seed.Plans.Add(plan);
            await seed.SaveChangesAsync();
        }

        await using var db = SubscriptionIntegrationDb.NewContext(Guid.Empty);
        var reader = new DirectPlanRateLimitReader(
            new PlanRateLimitRepository(db),
            new PlanRepository(db),
            new RateLimitDefaultProfileRepository(db),
            new NoCache()
        );

        var snapshot = await reader.GetAsync(plan.Code.Value, RateLimitCategory.K);

        Assert.NotNull(snapshot);
        Assert.Equal(20m, snapshot!.MultiplierOverride);
        Assert.Null(snapshot.HardOverridePerMinute);
        Assert.Empty(await db.PlanRateLimits.AsNoTracking().Where(row => row.PlanCode == plan.Code).ToListAsync());
    }

    private sealed class NoCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) => Task.FromResult<T?>(default);

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;

        public Task<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            TimeSpan? ttl = null,
            CancellationToken ct = default
        ) => factory(ct);
    }
}
