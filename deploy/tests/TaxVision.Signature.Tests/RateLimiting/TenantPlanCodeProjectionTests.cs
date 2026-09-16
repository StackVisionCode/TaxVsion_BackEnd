using BuildingBlocks.RateLimiting;
using TaxVision.Signature.Domain.RateLimiting;
using Xunit;

namespace TaxVision.Signature.Tests.RateLimiting;

/// <summary>
/// Piloto del gate de módulo (Fase 1) en Signature: la proyección local persiste, además del plan,
/// los módulos habilitados y los expone por <see cref="ITenantPlanCodeProjection.EnabledModules"/>.
/// </summary>
public sealed class TenantPlanCodeProjectionTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void Freshly_created_projection_has_no_modules()
    {
        var projection = TenantPlanCodeProjection.Create(Tenant, "free", 1);
        Assert.Empty(projection.EnabledModules);
    }

    [Fact]
    public void Three_arg_apply_persists_enabled_modules()
    {
        var projection = TenantPlanCodeProjection.Create(Tenant, "free", 1);

        projection.ApplyIfNewer("pro", 2, ["signatures", "documents"]);

        Assert.Equal("pro", projection.PlanCode);
        Assert.Equal(2, projection.RevisionNumber);
        Assert.Equal(["signatures", "documents"], projection.EnabledModules);
    }

    [Fact]
    public void Two_arg_apply_preserves_existing_modules()
    {
        var projection = TenantPlanCodeProjection.Create(Tenant, "free", 1);
        projection.ApplyIfNewer("pro", 2, ["signatures"]);

        // El overload de 2 args (sin módulos) no debe borrar los ya persistidos.
        projection.ApplyIfNewer("pro", 3);

        Assert.Equal(3, projection.RevisionNumber);
        Assert.Equal(["signatures"], projection.EnabledModules);
    }

    [Fact]
    public void Older_revision_is_ignored_for_plan_and_modules()
    {
        var projection = TenantPlanCodeProjection.Create(Tenant, "enterprise", 5);
        projection.ApplyIfNewer("enterprise", 5, ["signatures", "documents"]);

        // Evento fuera de orden: no degrada plan ni módulos.
        projection.ApplyIfNewer("free", 4, []);

        Assert.Equal("enterprise", projection.PlanCode);
        Assert.Equal(5, projection.RevisionNumber);
        Assert.Equal(["signatures", "documents"], projection.EnabledModules);
    }

    [Fact]
    public void Applying_empty_module_set_clears_modules()
    {
        var projection = TenantPlanCodeProjection.Create(Tenant, "pro", 1);
        projection.ApplyIfNewer("pro", 2, ["signatures"]);

        // Downgrade que quita el módulo (revisión más nueva): la lista queda vacía.
        projection.ApplyIfNewer("free", 3, []);

        Assert.Empty(projection.EnabledModules);
    }
}
