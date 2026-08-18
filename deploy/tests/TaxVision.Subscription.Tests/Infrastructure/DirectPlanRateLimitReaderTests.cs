using BuildingBlocks.Caching;
using BuildingBlocks.RateLimiting;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.RateLimiting;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Infrastructure.RateLimiting;
using Xunit;

namespace TaxVision.Subscription.Tests.Infrastructure;

/// <summary>
/// Auditoria RateLimit hallazgo #2 — DirectPlanRateLimitReader cierra el gap documentado en
/// AddRateLimitTierQuotas/Program.cs: Subscription resuelve su propio catálogo de PlanRateLimits
/// sin HTTP/M2M circular. Mismo shape de test que HttpPlanRateLimitReader hubiera tenido, contra
/// IPlanRateLimitRepository en vez de un HttpClient.
/// </summary>
public sealed class DirectPlanRateLimitReaderTests
{
    private static PlanRateLimit BuildRow(string planCode, RateLimitCategory category, decimal multiplier) =>
        PlanRateLimit.Seed(Guid.NewGuid(), PlanCode.Create(planCode).Value, category, multiplier).Value;

    [Fact]
    public async Task GetAsync_returns_the_snapshot_for_a_known_plan_and_category()
    {
        var repository = new FakeRepository([BuildRow("pro", RateLimitCategory.G, 3m)]);
        var reader = new DirectPlanRateLimitReader(repository, new PassThroughCache());

        var snapshot = await reader.GetAsync("pro", RateLimitCategory.G);

        Assert.NotNull(snapshot);
        Assert.Equal(3m, snapshot!.MultiplierOverride);
        Assert.Null(snapshot.HardOverridePerMinute);
    }

    [Fact]
    public async Task GetAsync_returns_null_when_the_plan_or_category_has_no_row()
    {
        var repository = new FakeRepository([BuildRow("pro", RateLimitCategory.G, 3m)]);
        var reader = new DirectPlanRateLimitReader(repository, new PassThroughCache());

        Assert.Null(await reader.GetAsync("starter", RateLimitCategory.G));
        Assert.Null(await reader.GetAsync("pro", RateLimitCategory.H));
    }

    [Fact]
    public async Task GetAsync_only_queries_the_repository_once_across_multiple_calls()
    {
        var repository = new FakeRepository([BuildRow("pro", RateLimitCategory.G, 3m)]);
        var reader = new DirectPlanRateLimitReader(repository, new PassThroughCache());

        await reader.GetAsync("pro", RateLimitCategory.G);
        await reader.GetAsync("pro", RateLimitCategory.G);

        Assert.Equal(1, repository.CallCount);
    }

    private sealed class FakeRepository(IReadOnlyList<PlanRateLimit> rows) : IPlanRateLimitRepository
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<PlanRateLimit>> GetAllAsync(CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(rows);
        }
    }

    // Cachea el resultado en memoria para el proceso del test (igual que ICacheService real,
    // solo sin Redis) — sin esto el test de "solo una llamada" no probaria nada real.
    private sealed class PassThroughCache : ICacheService
    {
        private readonly Dictionary<string, object> _store = [];

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
            Task.FromResult(_store.TryGetValue(key, out var value) ? (T?)value : default);

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
        {
            _store[key] = value!;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            _store.Remove(key);
            return Task.CompletedTask;
        }

        public async Task<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            TimeSpan? ttl = null,
            CancellationToken ct = default
        )
        {
            if (_store.TryGetValue(key, out var cached))
                return (T)cached;
            var value = await factory(ct);
            _store[key] = value!;
            return value;
        }
    }
}
