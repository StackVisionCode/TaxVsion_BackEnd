using BuildingBlocks.RateLimiting;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.RateLimit;

public sealed class RateLimitPolicyCatalogTests
{
    [Fact]
    public void All_policy_names_are_unique()
    {
        var names = RateLimitPolicyCatalog.All.Select(policy => policy.Name.Value).ToArray();

        Assert.Equal(names.Length, names.Distinct().Count());
    }

    // Primaria por tenant + overlay [Tenant] construyen la MISMA clave Redis: cada request se contaba dos
    // veces y el límite efectivo quedaba en la mitad (le pasaba a 3 políticas de Growth).
    [Fact]
    public void No_policy_overlays_the_same_tenant_bucket_as_its_primary_partition()
    {
        var offenders = RateLimitPolicyCatalog
            .All.Where(policy =>
                policy.OverlayQuotaPerMinute is not null
                && policy.PrimaryPartition == RateLimitPartitionDimension.Tenant
                && policy.OverlayLayers.SequenceEqual([RateLimitPartitionDimension.Tenant])
            )
            .Select(policy => policy.Name.Value)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Overlay duplicates the primary tenant bucket: " + string.Join(", ", offenders)
        );
    }

    // El tope global por endpoint no escala por plan y lo comparten todos los tenants: en listados y
    // búsquedas (H) era un techo arbitrario sobre lecturas que el overlay por tenant ya acota.
    [Fact]
    public void Only_bulk_policies_carry_a_global_endpoint_cap()
    {
        Assert.All(
            RateLimitPolicyCatalog.All.Where(policy => policy.EndpointCapPerWindow is not null),
            policy => Assert.Equal(RateLimitCategory.I, policy.Category)
        );
    }

    [Theory]
    [InlineData(RateLimitCategory.A)]
    [InlineData(RateLimitCategory.B)]
    [InlineData(RateLimitCategory.C)]
    [InlineData(RateLimitCategory.D)]
    [InlineData(RateLimitCategory.E)]
    [InlineData(RateLimitCategory.F)]
    [InlineData(RateLimitCategory.G)]
    [InlineData(RateLimitCategory.H)]
    [InlineData(RateLimitCategory.I)]
    [InlineData(RateLimitCategory.J)]
    [InlineData(RateLimitCategory.K)]
    [InlineData(RateLimitCategory.L)]
    [InlineData(RateLimitCategory.M)]
    [InlineData(RateLimitCategory.N)]
    [InlineData(RateLimitCategory.O)]
    public void Every_quota_bearing_category_has_at_least_one_seeded_policy(RateLimitCategory category)
    {
        Assert.Contains(RateLimitPolicyCatalog.All, policy => policy.Category == category);
    }

    [Theory]
    [InlineData(RateLimitCategory.P)]
    [InlineData(RateLimitCategory.Q)]
    public void Exempt_and_infra_categories_have_no_catalog_policy(RateLimitCategory category)
    {
        // P (health) es siempre exento, Q (load shedder) es infra de Gateway (Fase 5) — ninguna
        // vive como política de servicio en el catálogo, ver RateLimitCategory.
        Assert.DoesNotContain(RateLimitPolicyCatalog.All, policy => policy.Category == category);
    }

    [Fact]
    public void GetByName_resolves_a_known_policy()
    {
        var policy = RateLimitPolicyCatalog.GetByName("auth.a.login");

        Assert.Equal(RateLimitCategory.A, policy.Category);
        Assert.Equal(10, policy.BaseQuotaPerMinute);
        Assert.Equal(60, policy.WindowSeconds);
    }

    [Fact]
    public void GetByName_throws_for_an_unknown_policy()
    {
        Assert.Throws<KeyNotFoundException>(() => RateLimitPolicyCatalog.GetByName("does.not.exist"));
    }

    [Fact]
    public void Public_branding_policy_partitions_by_token_with_no_overlay()
    {
        // Pre-login no hay tenant ni user; el TieredRateLimitEvaluator solo soporta Token como
        // partición primaria sin JWT, y overlay únicamente [Tenant]. Este test fija esa restricción:
        // si alguien le agrega un overlay a esta política, el evaluador lanzaría en runtime.
        var policy = RateLimitPolicyCatalog.GetByName("tenant.d.branding_public");

        Assert.Equal(RateLimitCategory.D, policy.Category);
        Assert.Equal(RateLimitPartitionDimension.Token, policy.PrimaryPartition);
        Assert.Empty(policy.OverlayLayers);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Auth.A.Login")]
    [InlineData("auth.login")]
    [InlineData("auth.z.login")]
    [InlineData("auth.a.")]
    public void From_rejects_names_that_do_not_match_the_canonical_shape(string invalidName)
    {
        Assert.Throws<ArgumentException>(() => RateLimitPolicyName.From(invalidName));
    }
}
