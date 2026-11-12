using BuildingBlocks.Domain;

namespace TaxVision.Subscription.Domain.Plans.Events;

/// <summary>El plan se empezó a ofrecer en el listado público y en el alta.</summary>
public sealed record PlanPutOnSaleDomainEvent(Guid PlanId, string PlanCode, Guid ActorUserId, DateTime OccurredAtUtc)
    : IDomainEvent;
