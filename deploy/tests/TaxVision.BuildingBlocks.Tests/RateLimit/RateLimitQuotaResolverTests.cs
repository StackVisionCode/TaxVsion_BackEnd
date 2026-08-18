using BuildingBlocks.RateLimiting;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.RateLimit;

public sealed class RateLimitQuotaResolverTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    // Réplica de las 30 filas sembradas por Fase 1 (starter/pro/enterprise x F..O) — ver
    // PlanRateLimitTests.cs (Subscription.Tests) para la fuente de verdad de estos números.
    private static readonly Dictionary<
        (string PlanCode, RateLimitCategory Category),
        PlanRateLimitSnapshot
    > SeededRows = new()
    {
        [("starter", RateLimitCategory.F)] = new PlanRateLimitSnapshot(1.0m, null),
        [("starter", RateLimitCategory.I)] = new PlanRateLimitSnapshot(1.0m, null),
        [("starter", RateLimitCategory.M)] = new PlanRateLimitSnapshot(1.0m, null),
        [("pro", RateLimitCategory.F)] = new PlanRateLimitSnapshot(3.0m, null),
        [("pro", RateLimitCategory.I)] = new PlanRateLimitSnapshot(5.0m, null),
        [("pro", RateLimitCategory.M)] = new PlanRateLimitSnapshot(1.0m, null),
        [("enterprise", RateLimitCategory.F)] = new PlanRateLimitSnapshot(10.0m, null),
        [("enterprise", RateLimitCategory.K)] = new PlanRateLimitSnapshot(20.0m, null),
        [("enterprise", RateLimitCategory.M)] = new PlanRateLimitSnapshot(1.0m, null),
    };

    [Theory]
    [InlineData(RateLimitCategory.A)]
    [InlineData(RateLimitCategory.B)]
    [InlineData(RateLimitCategory.C)]
    [InlineData(RateLimitCategory.D)]
    [InlineData(RateLimitCategory.E)]
    public async Task Pre_auth_categories_never_scale_and_never_touch_the_readers(RateLimitCategory category)
    {
        var policy = Policy(category, baseQuota: 10, windowSeconds: 60);
        var planCodeReader = new ThrowingTenantPlanCodeReader();
        var rateLimitReader = new ThrowingPlanRateLimitReader();
        var resolver = new RateLimitQuotaResolver(planCodeReader, rateLimitReader);

        var quota = await resolver.ResolveAsync(policy, TenantId);

        Assert.Equal(10, quota.PermitCount);
        Assert.Equal(60, quota.WindowSeconds);
        Assert.False(quota.IsFallback);
    }

    [Theory]
    [InlineData("starter", RateLimitCategory.F, 300, 300)]
    [InlineData("starter", RateLimitCategory.I, 5, 5)]
    [InlineData("starter", RateLimitCategory.M, 5, 5)]
    [InlineData("pro", RateLimitCategory.F, 300, 900)]
    [InlineData("pro", RateLimitCategory.I, 5, 25)]
    [InlineData("pro", RateLimitCategory.M, 5, 5)]
    [InlineData("enterprise", RateLimitCategory.F, 300, 3000)]
    [InlineData("enterprise", RateLimitCategory.K, 60, 1200)]
    [InlineData("enterprise", RateLimitCategory.M, 5, 5)]
    public async Task Scaling_categories_apply_the_plan_multiplier(
        string planCode,
        RateLimitCategory category,
        int baseQuota,
        int expectedQuota
    )
    {
        var policy = Policy(category, baseQuota, windowSeconds: 60);
        var resolver = new RateLimitQuotaResolver(
            new FakeTenantPlanCodeReader(planCode),
            new FakePlanRateLimitReader(SeededRows)
        );

        var quota = await resolver.ResolveAsync(policy, TenantId);

        Assert.Equal(expectedQuota, quota.PermitCount);
        Assert.False(quota.IsFallback);
    }

    [Fact]
    public async Task Hard_override_wins_over_multiplier()
    {
        var policy = Policy(RateLimitCategory.M, baseQuota: 5, windowSeconds: 60);
        var rows = new Dictionary<(string, RateLimitCategory), PlanRateLimitSnapshot>
        {
            [("custom", RateLimitCategory.M)] = new PlanRateLimitSnapshot(1.0m, HardOverridePerMinute: 50),
        };
        var resolver = new RateLimitQuotaResolver(
            new FakeTenantPlanCodeReader("custom"),
            new FakePlanRateLimitReader(rows)
        );

        var quota = await resolver.ResolveAsync(policy, TenantId);

        Assert.Equal(50, quota.PermitCount);
        Assert.False(quota.IsFallback);
    }

    [Fact]
    public async Task Unknown_tenant_falls_open_to_base_quota()
    {
        var policy = Policy(RateLimitCategory.F, baseQuota: 300, windowSeconds: 60);
        var resolver = new RateLimitQuotaResolver(
            new FakeTenantPlanCodeReader(null),
            new ThrowingPlanRateLimitReader()
        );

        var quota = await resolver.ResolveAsync(policy, TenantId);

        Assert.Equal(300, quota.PermitCount);
        Assert.True(quota.IsFallback);
    }

    [Fact]
    public async Task Unknown_plan_or_category_combination_falls_open_to_base_quota()
    {
        var policy = Policy(RateLimitCategory.F, baseQuota: 300, windowSeconds: 60);
        var resolver = new RateLimitQuotaResolver(
            new FakeTenantPlanCodeReader("some-unseeded-plan"),
            new FakePlanRateLimitReader(SeededRows)
        );

        var quota = await resolver.ResolveAsync(policy, TenantId);

        Assert.Equal(300, quota.PermitCount);
        Assert.True(quota.IsFallback);
    }

    private static RateLimitPolicyDefinition Policy(RateLimitCategory category, int baseQuota, int windowSeconds) =>
        new()
        {
            Name = RateLimitPolicyName.From("customer.g.create"),
            Category = category,
            PrimaryPartition = RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
            BaseQuotaPerMinute = baseQuota,
            WindowSeconds = windowSeconds,
            Algorithm = RateLimitAlgorithm.TokenBucket,
        };

    private sealed class FakeTenantPlanCodeReader(string? planCode) : ITenantPlanCodeReader
    {
        public Task<string?> GetPlanCodeAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(planCode);
    }

    private sealed class ThrowingTenantPlanCodeReader : ITenantPlanCodeReader
    {
        public Task<string?> GetPlanCodeAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new InvalidOperationException("Should not be called for a category that never scales by plan.");
    }

    private sealed class FakePlanRateLimitReader(
        IReadOnlyDictionary<(string, RateLimitCategory), PlanRateLimitSnapshot> rows
    ) : IPlanRateLimitReader
    {
        public Task<PlanRateLimitSnapshot?> GetAsync(
            string planCode,
            RateLimitCategory category,
            CancellationToken ct = default
        ) => Task.FromResult(rows.GetValueOrDefault((planCode, category)));
    }

    private sealed class ThrowingPlanRateLimitReader : IPlanRateLimitReader
    {
        public Task<PlanRateLimitSnapshot?> GetAsync(
            string planCode,
            RateLimitCategory category,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("Should not be called for a category that never scales by plan.");
    }
}
