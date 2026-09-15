using TaxVision.Calendar.Domain.RateLimiting;
using Xunit;

namespace TaxVision.Calendar.Tests.RateLimiting;

public sealed class TenantPlanCodeProjectionTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void Freshly_created_projection_has_no_modules()
    {
        var p = TenantPlanCodeProjection.Create(Tenant, "free", 1);
        Assert.Empty(p.EnabledModules);
    }

    [Fact]
    public void Three_arg_apply_persists_enabled_modules()
    {
        var p = TenantPlanCodeProjection.Create(Tenant, "free", 1);
        p.ApplyIfNewer("pro", 2, ["alpha", "beta"]);
        Assert.Equal("pro", p.PlanCode);
        Assert.Equal(2, p.RevisionNumber);
        Assert.Equal(["alpha", "beta"], p.EnabledModules);
    }

    [Fact]
    public void Two_arg_apply_preserves_existing_modules()
    {
        var p = TenantPlanCodeProjection.Create(Tenant, "free", 1);
        p.ApplyIfNewer("pro", 2, ["alpha"]);
        p.ApplyIfNewer("pro", 3);
        Assert.Equal(3, p.RevisionNumber);
        Assert.Equal(["alpha"], p.EnabledModules);
    }

    [Fact]
    public void Older_revision_is_ignored_for_plan_and_modules()
    {
        var p = TenantPlanCodeProjection.Create(Tenant, "enterprise", 5);
        p.ApplyIfNewer("enterprise", 5, ["alpha", "beta"]);
        p.ApplyIfNewer("free", 4, []);
        Assert.Equal("enterprise", p.PlanCode);
        Assert.Equal(5, p.RevisionNumber);
        Assert.Equal(["alpha", "beta"], p.EnabledModules);
    }

    [Fact]
    public void Applying_empty_module_set_clears_modules()
    {
        var p = TenantPlanCodeProjection.Create(Tenant, "pro", 1);
        p.ApplyIfNewer("pro", 2, ["alpha"]);
        p.ApplyIfNewer("free", 3, []);
        Assert.Empty(p.EnabledModules);
    }
}
