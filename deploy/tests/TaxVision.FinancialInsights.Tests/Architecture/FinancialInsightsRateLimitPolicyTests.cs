using BuildingBlocks.RateLimiting;

namespace TaxVision.FinancialInsights.Tests.Architecture;

// Cupos por acción: generosos para el uso real, acotados por usuario y por oficina.
public sealed class FinancialInsightsRateLimitPolicyTests
{
    [Theory]
    [InlineData("financial.i.import_batch_create", 60, 600, 600)]
    [InlineData("financial.i.portal_import_batch_create", 30, 600, 600)]
    [InlineData("financial.i.import_document_retry", 60, 600, 600)]
    [InlineData("financial.f.import_batch_read", 300, 60, 3000)]
    public void Policies_have_the_agreed_quotas(string name, int quota, int windowSeconds, int overlay)
    {
        var policy = RateLimitPolicyCatalog.GetByName(name);

        Assert.Equal(quota, policy.BaseQuotaPerMinute);
        Assert.Equal(windowSeconds, policy.WindowSeconds);
        Assert.Equal(overlay, policy.OverlayQuotaPerMinute);
        Assert.Equal(RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User, policy.PrimaryPartition);
        Assert.Equal([RateLimitPartitionDimension.Tenant], policy.OverlayLayers);
    }
}
