using BuildingBlocks.RateLimiting;
using Xunit;

namespace TaxVision.Correspondence.Tests.RateLimiting;

// La política de POST /correspondence/files/visibility existe: un nombre fuera del catálogo revienta en ejecución.
public sealed class CorrespondenceFileVisibilityRateLimitTests
{
    [Fact]
    public void File_visibility_policy_is_registered_with_its_own_quota()
    {
        var policy = RateLimitPolicyCatalog.GetByName("correspondence.f.file_visibility");

        Assert.Equal(RateLimitCategory.F, policy.Category);
        Assert.Equal(RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User, policy.PrimaryPartition);
        Assert.Equal([RateLimitPartitionDimension.Tenant], policy.OverlayLayers);
        Assert.Equal(600, policy.BaseQuotaPerMinute);
        Assert.Equal(6000, policy.OverlayQuotaPerMinute);
    }
}
