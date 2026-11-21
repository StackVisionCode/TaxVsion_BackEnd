using BuildingBlocks.RateLimiting;

namespace TaxVision.Notes.Tests.RateLimiting;

/// <summary>La cuota de la visibilidad por archivo existe y no comparte la de los listados de Notes.</summary>
public sealed class NotesFileVisibilityRateLimitTests
{
    [Fact]
    public void The_file_visibility_policy_is_registered_with_its_quota()
    {
        var policy = RateLimitPolicyCatalog.GetByName("notes.f.file_visibility");

        Assert.Equal(RateLimitCategory.F, policy.Category);
        Assert.Equal(RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User, policy.PrimaryPartition);
        Assert.Equal([RateLimitPartitionDimension.Tenant], policy.OverlayLayers);
        Assert.Equal(600, policy.BaseQuotaPerMinute);
        Assert.Equal(6000, policy.OverlayQuotaPerMinute);
    }
}
