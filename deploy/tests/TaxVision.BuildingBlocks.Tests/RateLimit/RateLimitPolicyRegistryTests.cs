using BuildingBlocks.RateLimiting;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.RateLimit;

public sealed class RateLimitPolicyRegistryTests
{
    private readonly IRateLimitPolicyRegistry registry = new RateLimitPolicyRegistry();

    [Fact]
    public void GetByName_delegates_to_the_static_catalog()
    {
        var policy = registry.GetByName("auth.a.login");

        Assert.Equal(RateLimitCategory.A, policy.Category);
    }

    [Fact]
    public void All_delegates_to_the_static_catalog()
    {
        Assert.Equal(RateLimitPolicyCatalog.All.Count, registry.All.Count);
    }

    [Fact]
    public void GetByName_throws_for_an_unknown_policy()
    {
        Assert.Throws<KeyNotFoundException>(() => registry.GetByName("does.not.exist"));
    }
}
