using BuildingBlocks.Domain;

namespace TaxVision.Subscription.Domain.Plans.Events;

/// <summary>El plan pasó a destacarse (o dejó de destacarse) en la vitrina.</summary>
public sealed record PlanFeaturedChangedDomainEvent(
    Guid PlanId,
    string PlanCode,
    bool IsFeatured,
    Guid ActorUserId,
    DateTime OccurredAtUtc
) : IDomainEvent;
