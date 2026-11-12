using BuildingBlocks.Domain;

namespace TaxVision.Subscription.Domain.Plans.Events;

/// <summary>El plan dejó de ofrecerse. Quien ya lo tiene lo conserva.</summary>
public sealed record PlanWithdrawnFromSaleDomainEvent(
    Guid PlanId,
    string PlanCode,
    Guid ActorUserId,
    DateTime OccurredAtUtc
) : IDomainEvent;
