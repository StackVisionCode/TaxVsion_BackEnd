using TaxVision.Mobile.Domain.RateLimiting;

namespace TaxVision.Mobile.Tests.Domain;

public sealed class TenantPlanCodeProjectionTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void Freshly_created_projection_has_no_modules()
    {
        var p = TenantPlanCodeProjection.Create(Tenant, "enterprise", 1);
        Assert.Empty(p.EnabledModules);
    }

    [Fact]
    public void Three_arg_apply_persists_enabled_modules()
    {
        var p = TenantPlanCodeProjection.Create(Tenant, "enterprise", 1);
        p.ApplyIfNewer("enterprise", 2, ["mileage", "documents"]);
        Assert.Equal(2, p.RevisionNumber);
        Assert.Equal(["mileage", "documents"], p.EnabledModules);
    }

    [Fact]
    public void Two_arg_apply_preserves_existing_modules()
    {
        var p = TenantPlanCodeProjection.Create(Tenant, "enterprise", 1);
        p.ApplyIfNewer("enterprise", 2, ["mileage"]);
        p.ApplyIfNewer("enterprise", 3);
        Assert.Equal(3, p.RevisionNumber);
        Assert.Equal(["mileage"], p.EnabledModules);
    }

    [Fact]
    public void Older_revision_is_ignored_for_plan_and_modules()
    {
        var p = TenantPlanCodeProjection.Create(Tenant, "enterprise", 5);
        p.ApplyIfNewer("enterprise", 5, ["mileage"]);
        p.ApplyIfNewer("starter", 4, []);
        Assert.Equal("enterprise", p.PlanCode);
        Assert.Equal(["mileage"], p.EnabledModules);
    }

    [Fact]
    public void Applying_empty_module_set_clears_modules()
    {
        var p = TenantPlanCodeProjection.Create(Tenant, "enterprise", 1);
        p.ApplyIfNewer("enterprise", 2, ["mileage"]);
        p.ApplyIfNewer("enterprise", 3, []);
        Assert.Empty(p.EnabledModules);
    }
}
