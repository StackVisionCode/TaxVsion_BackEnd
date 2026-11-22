using BuildingBlocks.RateLimiting;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.RateLimit;

// Preferencias del espacio de trabajo: cupo propio para no compartir el overlay de marca y perfil.
public sealed class TenantRateLimitPolicyTests
{
    [Theory]
    [InlineData("tenant.f.preferences_read", RateLimitCategory.F, 300, 60, 3000)]
    [InlineData("tenant.g.preferences_write", RateLimitCategory.G, 60, 60, 600)]
    public void Policies_have_the_agreed_quotas(
        string name,
        RateLimitCategory category,
        int quota,
        int windowSeconds,
        int overlay
    )
    {
        var policy = RateLimitPolicyCatalog.GetByName(name);

        Assert.Equal(category, policy.Category);
        Assert.Equal(quota, policy.BaseQuotaPerMinute);
        Assert.Equal(windowSeconds, policy.WindowSeconds);
        Assert.Equal(overlay, policy.OverlayQuotaPerMinute);
        Assert.Equal(RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User, policy.PrimaryPartition);
        Assert.Equal([RateLimitPartitionDimension.Tenant], policy.OverlayLayers);
        Assert.Equal(RateLimitAlgorithm.TokenBucket, policy.Algorithm);
    }
}
