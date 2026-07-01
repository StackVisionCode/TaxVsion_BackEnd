using BuildingBlocks.Domain;

namespace TaxVision.Subscription.Domain.Plans;

public sealed class PlanFeature : BaseEntity
{
    public Guid PlanId { get; init; }          // apunta al Plan, no a una versión
    public string FeatureCode { get; init; } = default!;
}
