using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.RateLimiting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.RateLimit;

[Collection(RateLimitMetricsCollection.Name)]
public sealed class TieredRateLimitEvaluatorTests
{
    private readonly Guid tenantId = Guid.NewGuid();
    private readonly Guid userId = Guid.NewGuid();

    [Fact]
    public async Task Allows_when_both_layers_are_within_quota()
    {
        var counter = new FakeAlgorithmCounter();
        var evaluator = new TieredRateLimitEvaluator(
            counter,
            new FixedQuotaResolver(new EffectiveQuota(5, 60, OverlayPermitCount: 50)),
            new RateLimitMetrics(),
            NullLogger<TieredRateLimitEvaluator>.Instance
        );

        var verdict = await evaluator.EvaluateAsync(Policy(), tenantId, userId);

        Assert.False(verdict.IsExceeded);
    }

    [Fact]
    public async Task Primary_layer_reports_user_when_it_exceeds_first()
    {
        var counter = new FakeAlgorithmCounter();
        var evaluator = new TieredRateLimitEvaluator(
            counter,
            new FixedQuotaResolver(new EffectiveQuota(2, 60, OverlayPermitCount: 50)),
            new RateLimitMetrics(),
            NullLogger<TieredRateLimitEvaluator>.Instance
        );
        var policy = Policy();

        await evaluator.EvaluateAsync(policy, tenantId, userId);
        await evaluator.EvaluateAsync(policy, tenantId, userId);
        var verdict = await evaluator.EvaluateAsync(policy, tenantId, userId);

        Assert.True(verdict.IsExceeded);
        Assert.Equal("user", verdict.Layer);
        Assert.Equal(2, verdict.Limit);
    }

    [Fact]
    public async Task Overlay_layer_reports_tenant_when_primary_is_fine_but_overlay_exceeds()
    {
        var counter = new FakeAlgorithmCounter();
        // Cuota primaria alta (nunca dispara), overlay bajo (dispara al 3er request).
        var evaluator = new TieredRateLimitEvaluator(
            counter,
            new FixedQuotaResolver(new EffectiveQuota(1000, 60, OverlayPermitCount: 2)),
            new RateLimitMetrics(),
            NullLogger<TieredRateLimitEvaluator>.Instance
        );
        var policy = Policy();

        await evaluator.EvaluateAsync(policy, tenantId, userId);
        await evaluator.EvaluateAsync(policy, tenantId, Guid.NewGuid()); // mismo tenant, otro user
        var verdict = await evaluator.EvaluateAsync(policy, tenantId, Guid.NewGuid());

        Assert.True(verdict.IsExceeded);
        Assert.Equal("tenant", verdict.Layer);
        Assert.Equal(2, verdict.Limit);
    }

    [Fact]
    public async Task Does_not_evaluate_overlay_when_policy_has_none()
    {
        var counter = new FakeAlgorithmCounter();
        var evaluator = new TieredRateLimitEvaluator(
            counter,
            new FixedQuotaResolver(new EffectiveQuota(5, 60)),
            new RateLimitMetrics(),
            NullLogger<TieredRateLimitEvaluator>.Instance
        );

        var verdict = await evaluator.EvaluateAsync(Policy(), tenantId, userId);

        Assert.False(verdict.IsExceeded);
        Assert.Single(counter.EvaluatedKeys); // solo la capa primaria, sin overlay
    }

    [Fact]
    public async Task Fails_open_when_the_algorithm_counter_throws()
    {
        var evaluator = new TieredRateLimitEvaluator(
            new ThrowingAlgorithmCounter(),
            new FixedQuotaResolver(new EffectiveQuota(1, 60)),
            new RateLimitMetrics(),
            NullLogger<TieredRateLimitEvaluator>.Instance
        );

        var verdict = await evaluator.EvaluateAsync(Policy(), tenantId, userId);

        Assert.False(verdict.IsExceeded);
    }

    [Fact]
    public async Task Fails_open_with_base_quota_when_the_quota_resolver_throws()
    {
        // Hallazgo #4 de la auditoría RateLimit — ResolveAsync no estaba protegido; un fallo de
        // caché de plan/token M2M/HTTP a Subscription/deserialización debía traducirse en un 500
        // en vez de caer al cupo base como el resto de las fuentes de fallback-open.
        var counter = new FakeAlgorithmCounter();
        var evaluator = new TieredRateLimitEvaluator(
            counter,
            new ThrowingQuotaResolver(),
            new RateLimitMetrics(),
            NullLogger<TieredRateLimitEvaluator>.Instance
        );
        var policy = Policy() with { BaseQuotaPerMinute = 3 };

        var verdict = await evaluator.EvaluateAsync(policy, tenantId, userId);

        Assert.False(verdict.IsExceeded);
        Assert.Single(counter.EvaluatedKeys); // evaluó la capa primaria con la cuota base, no crasheó
    }

    [Fact]
    public async Task Throws_for_a_primary_partition_it_does_not_support()
    {
        var counter = new FakeAlgorithmCounter();
        var evaluator = new TieredRateLimitEvaluator(
            counter,
            new FixedQuotaResolver(new EffectiveQuota(5, 60)),
            new RateLimitMetrics(),
            NullLogger<TieredRateLimitEvaluator>.Instance
        );
        var unsupportedPolicy = Policy(RateLimitPartitionDimension.AccountOrProvider);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            evaluator.EvaluateAsync(unsupportedPolicy, tenantId, userId)
        );
    }

    [Fact]
    public async Task Throws_for_the_composite_K_partition_instead_of_silently_dropping_AccountOrProvider()
    {
        // Hallazgo #12 de la auditoría post-Fase-9 — Tenant|AccountOrProvider (categoría K) pasaba
        // silenciosamente por la rama "solo Tenant" con el HasFlag anterior, en vez de lanzar.
        var counter = new FakeAlgorithmCounter();
        var evaluator = new TieredRateLimitEvaluator(
            counter,
            new FixedQuotaResolver(new EffectiveQuota(5, 60)),
            new RateLimitMetrics(),
            NullLogger<TieredRateLimitEvaluator>.Instance
        );
        var kPolicy = Policy(RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.AccountOrProvider);

        await Assert.ThrowsAsync<NotSupportedException>(() => evaluator.EvaluateAsync(kPolicy, tenantId, userId));
    }

    [Fact]
    public async Task Throws_when_overlay_layers_declare_something_other_than_Tenant()
    {
        // Hallazgo #12 — OverlayLayers se declaraba pero nunca se leía; validar en vez de asumir.
        var counter = new FakeAlgorithmCounter();
        var evaluator = new TieredRateLimitEvaluator(
            counter,
            new FixedQuotaResolver(new EffectiveQuota(5, 60, OverlayPermitCount: 50)),
            new RateLimitMetrics(),
            NullLogger<TieredRateLimitEvaluator>.Instance
        );
        var policy = Policy() with { OverlayLayers = [RateLimitPartitionDimension.Ip] };

        await Assert.ThrowsAsync<NotSupportedException>(() => evaluator.EvaluateAsync(policy, tenantId, userId));
    }

    [Fact]
    public async Task Endpoint_cap_layer_trips_before_primary_or_overlay_and_reports_endpoint()
    {
        // Capa 4 (hallazgo #7) — cap agregado a través de todos los tenants, evaluado primero.
        var counter = new FakeAlgorithmCounter();
        var evaluator = new TieredRateLimitEvaluator(
            counter,
            new FixedQuotaResolver(new EffectiveQuota(1000, 60, OverlayPermitCount: 1000)),
            new RateLimitMetrics(),
            NullLogger<TieredRateLimitEvaluator>.Instance
        );
        var policy = Policy() with { EndpointCapPerWindow = 1 };

        await evaluator.EvaluateAsync(policy, tenantId, userId);
        var verdict = await evaluator.EvaluateAsync(policy, Guid.NewGuid(), Guid.NewGuid()); // otro tenant — igual dispara

        Assert.True(verdict.IsExceeded);
        Assert.Equal("endpoint", verdict.Layer);
        Assert.Equal(1, verdict.Limit);
    }

    [Fact]
    public async Task Endpoint_cap_is_not_evaluated_when_policy_has_none()
    {
        var counter = new FakeAlgorithmCounter();
        var evaluator = new TieredRateLimitEvaluator(
            counter,
            new FixedQuotaResolver(new EffectiveQuota(5, 60)),
            new RateLimitMetrics(),
            NullLogger<TieredRateLimitEvaluator>.Instance
        );

        await evaluator.EvaluateAsync(Policy(), tenantId, userId);

        Assert.DoesNotContain(counter.EvaluatedKeys, k => k.Contains(":endpoint"));
    }

    private static RateLimitPolicyDefinition Policy(
        RateLimitPartitionDimension primary = RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User
    ) =>
        new()
        {
            Name = RateLimitPolicyName.From("customer.g.create"),
            Category = RateLimitCategory.G,
            PrimaryPartition = primary,
            OverlayLayers = [RateLimitPartitionDimension.Tenant],
            BaseQuotaPerMinute = 5,
            WindowSeconds = 60,
            Algorithm = RateLimitAlgorithm.TokenBucket,
        };

    private sealed class FixedQuotaResolver(EffectiveQuota quota) : IRateLimitQuotaResolver
    {
        public Task<EffectiveQuota> ResolveAsync(
            RateLimitPolicyDefinition policy,
            Guid tenantId,
            CancellationToken ct = default
        ) => Task.FromResult(quota);
    }

    private sealed class ThrowingQuotaResolver : IRateLimitQuotaResolver
    {
        public Task<EffectiveQuota> ResolveAsync(
            RateLimitPolicyDefinition policy,
            Guid tenantId,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("Subscription M2M call failed.");
    }

    private sealed class FakeAlgorithmCounter : IRateLimitAlgorithmCounter
    {
        private readonly Dictionary<string, long> counts = [];

        public List<string> EvaluatedKeys { get; } = [];

        public Task<bool> EvaluateAsync(
            RateCounterKey key,
            RateLimitAlgorithm algorithm,
            int limit,
            TimeSpan window,
            CancellationToken ct = default
        )
        {
            EvaluatedKeys.Add(key.Value);
            counts.TryGetValue(key.Value, out var current);
            counts[key.Value] = current + 1;
            return Task.FromResult(counts[key.Value] > limit);
        }
    }

    private sealed class ThrowingAlgorithmCounter : IRateLimitAlgorithmCounter
    {
        public Task<bool> EvaluateAsync(
            RateCounterKey key,
            RateLimitAlgorithm algorithm,
            int limit,
            TimeSpan window,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("Redis is down.");
    }
}
