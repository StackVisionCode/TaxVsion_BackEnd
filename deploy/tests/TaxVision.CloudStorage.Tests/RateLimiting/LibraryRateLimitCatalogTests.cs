using BuildingBlocks.RateLimiting;

namespace TaxVision.CloudStorage.Tests.RateLimiting;

public sealed class LibraryRateLimitCatalogTests
{
    [Theory]
    [InlineData("cloudstorage.f.library_sources", RateLimitCategory.F, 300, 3000)]
    [InlineData("cloudstorage.h.library_search", RateLimitCategory.H, 120, 1200)]
    [InlineData("cloudstorage.f.library_download", RateLimitCategory.F, 300, 3000)]
    public void The_library_policies_are_registered(
        string name,
        RateLimitCategory category,
        int quota,
        int overlayQuota
    )
    {
        var policy = RateLimitPolicyCatalog.GetByName(name);

        Assert.Equal(category, policy.Category);
        Assert.Equal(quota, policy.BaseQuotaPerMinute);
        Assert.Equal(overlayQuota, policy.OverlayQuotaPerMinute);
        Assert.Equal(60, policy.WindowSeconds);
        Assert.Equal(RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User, policy.PrimaryPartition);
    }
}
