using BuildingBlocks.RateLimiting;
using TaxVision.Subscription.Domain.RateLimiting;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

public sealed class PlanRateLimitTests
{
    [Fact]
    public void Seed_accepts_a_positive_multiplier()
    {
        var result = PlanRateLimit.Seed(Guid.NewGuid(), StarterCode(), RateLimitCategory.F, 1.0m);

        Assert.True(result.IsSuccess);
        Assert.Equal(1.0m, result.Value.MultiplierOverride);
        Assert.Null(result.Value.HardOverridePerMinute);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Seed_rejects_a_non_positive_multiplier(decimal multiplier)
    {
        var result = PlanRateLimit.Seed(Guid.NewGuid(), StarterCode(), RateLimitCategory.F, multiplier);

        Assert.True(result.IsFailure);
        Assert.Equal("PlanRateLimit.InvalidMultiplier", result.Error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Seed_rejects_a_non_positive_hard_override_when_provided(int hardOverride)
    {
        var result = PlanRateLimit.Seed(Guid.NewGuid(), StarterCode(), RateLimitCategory.F, 1.0m, hardOverride);

        Assert.True(result.IsFailure);
        Assert.Equal("PlanRateLimit.InvalidHardOverride", result.Error.Code);
    }

    // §5 del plan: 3 planes reales (starter/pro/enterprise) x las 10 categorías con tenant
    // (F..O) = 30 combinaciones sembradas en la migración AddPlanRateLimits. Cada una debe
    // resolver una cuota efectiva positiva combinando el multiplicador con la cuota base de
    // la política representativa de esa categoría en el catálogo — la fórmula real de
    // resolución (tier del tenant, caché, fallback) es responsabilidad de Fase 2, esto valida
    // solo que los datos sembrados son consistentes con el catálogo hoy.
    [Theory]
    [MemberData(nameof(SeededPlanCategoryCombinations))]
    public void Every_seeded_plan_and_category_resolves_a_positive_effective_quota(
        string planCode,
        RateLimitCategory category,
        decimal multiplier
    )
    {
        var planRateLimit = PlanRateLimit
            .Seed(Guid.NewGuid(), PlanCode.Create(planCode).Value, category, multiplier)
            .Value;
        var policiesForCategory = RateLimitPolicyCatalog.All.Where(policy => policy.Category == category).ToArray();

        Assert.NotEmpty(policiesForCategory);
        Assert.All(
            policiesForCategory,
            policy => Assert.True(policy.BaseQuotaPerMinute * planRateLimit.MultiplierOverride > 0)
        );
    }

    [Theory]
    [InlineData("starter")]
    [InlineData("pro")]
    [InlineData("enterprise")]
    public void M_and_N_never_scale_regardless_of_plan(string planCode)
    {
        var moneyOut = SeedRow(planCode, RateLimitCategory.M);
        var reveal = SeedRow(planCode, RateLimitCategory.N);

        Assert.Equal(1.0m, moneyOut.Multiplier);
        Assert.Equal(1.0m, reveal.Multiplier);
    }

    public static IEnumerable<object[]> SeededPlanCategoryCombinations()
    {
        foreach (var (planCode, category, multiplier) in AllSeedRows())
            yield return [planCode, category, multiplier];
    }

    private static (string PlanCode, RateLimitCategory Category, decimal Multiplier) SeedRow(
        string planCode,
        RateLimitCategory category
    ) => AllSeedRows().Single(row => row.PlanCode == planCode && row.Category == category);

    // Réplica 1:1 de las filas sembradas por PlanRateLimitConfiguration/AddPlanRateLimits — si
    // esta lista se desincroniza de la migración real, el objetivo es que alguien lo note al
    // tocar cualquiera de los dos lados, no que quede sembrado silenciosamente distinto.
    private static IEnumerable<(string PlanCode, RateLimitCategory Category, decimal Multiplier)> AllSeedRows()
    {
        (string, RateLimitCategory, decimal)[] rows =
        [
            ("starter", RateLimitCategory.F, 1.0m),
            ("starter", RateLimitCategory.G, 1.0m),
            ("starter", RateLimitCategory.H, 1.0m),
            ("starter", RateLimitCategory.I, 1.0m),
            ("starter", RateLimitCategory.J, 1.0m),
            ("starter", RateLimitCategory.K, 1.0m),
            ("starter", RateLimitCategory.L, 1.0m),
            ("starter", RateLimitCategory.M, 1.0m),
            ("starter", RateLimitCategory.N, 1.0m),
            ("starter", RateLimitCategory.O, 1.0m),
            ("pro", RateLimitCategory.F, 3.0m),
            ("pro", RateLimitCategory.G, 3.0m),
            ("pro", RateLimitCategory.H, 3.0m),
            ("pro", RateLimitCategory.I, 5.0m),
            ("pro", RateLimitCategory.J, 5.0m),
            ("pro", RateLimitCategory.K, 3.0m),
            ("pro", RateLimitCategory.L, 3.0m),
            ("pro", RateLimitCategory.M, 1.0m),
            ("pro", RateLimitCategory.N, 1.0m),
            ("pro", RateLimitCategory.O, 3.0m),
            ("enterprise", RateLimitCategory.F, 10.0m),
            ("enterprise", RateLimitCategory.G, 10.0m),
            ("enterprise", RateLimitCategory.H, 15.0m),
            ("enterprise", RateLimitCategory.I, 10.0m),
            ("enterprise", RateLimitCategory.J, 10.0m),
            ("enterprise", RateLimitCategory.K, 20.0m),
            ("enterprise", RateLimitCategory.L, 10.0m),
            ("enterprise", RateLimitCategory.M, 1.0m),
            ("enterprise", RateLimitCategory.N, 1.0m),
            ("enterprise", RateLimitCategory.O, 10.0m),
        ];

        return rows;
    }

    private static PlanCode StarterCode() => PlanCode.Create("starter").Value;
}
