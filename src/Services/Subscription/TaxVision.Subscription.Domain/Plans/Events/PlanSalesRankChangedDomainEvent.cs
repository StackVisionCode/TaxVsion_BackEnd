using BuildingBlocks.Domain;

namespace TaxVision.Subscription.Domain.Plans.Events;

/// <summary>Cambió la posición del plan en la vitrina.</summary>
public sealed record PlanSalesRankChangedDomainEvent(
    Guid PlanId,
    string PlanCode,
    int PreviousRank,
    int Rank,
    Guid ActorUserId,
    DateTime OccurredAtUtc
) : IDomainEvent;
