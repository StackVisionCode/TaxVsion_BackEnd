using TaxVision.Customer.Domain.RateLimiting;

namespace TaxVision.Customer.Tests.Domain;

/// <summary>
/// RateLimit Fase 6 — TenantEntitlementsChangedIntegrationEvent no garantiza orden de entrega
/// (RabbitMQ, reintentos). ApplyIfNewer debe ser idempotente por RevisionNumber, igual que
/// UserPermissionsProjection.ApplyIfNewer con PermissionsVersion.
/// </summary>
public sealed class TenantPlanCodeProjectionTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void ApplyIfNewer_with_higher_revision_updates_plan_code()
    {
        var projection = TenantPlanCodeProjection.Create(TenantId, "starter", revisionNumber: 1);

        projection.ApplyIfNewer("pro", revisionNumber: 2);

        Assert.Equal("pro", projection.PlanCode);
        Assert.Equal(2, projection.RevisionNumber);
    }

    [Fact]
    public void ApplyIfNewer_with_older_revision_is_ignored()
    {
        var projection = TenantPlanCodeProjection.Create(TenantId, "pro", revisionNumber: 5);

        projection.ApplyIfNewer("starter", revisionNumber: 3);

        Assert.Equal("pro", projection.PlanCode);
        Assert.Equal(5, projection.RevisionNumber);
    }

    [Fact]
    public void ApplyIfNewer_with_same_revision_still_applies_for_at_least_once_delivery()
    {
        var projection = TenantPlanCodeProjection.Create(TenantId, "pro", revisionNumber: 5);

        projection.ApplyIfNewer("pro", revisionNumber: 5);

        Assert.Equal("pro", projection.PlanCode);
        Assert.Equal(5, projection.RevisionNumber);
    }
}
