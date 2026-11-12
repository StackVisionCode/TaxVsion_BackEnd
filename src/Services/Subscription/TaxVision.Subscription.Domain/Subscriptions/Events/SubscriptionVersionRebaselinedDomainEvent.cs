using BuildingBlocks.Domain;

namespace TaxVision.Subscription.Domain.Subscriptions.Events;

/// <summary>La oficina pasó a otra edición de su mismo plan con los mismos términos comerciales (R1).</summary>
public sealed record SubscriptionVersionRebaselinedDomainEvent(
    Guid TenantId,
    Guid TenantSubscriptionId,
    Guid PlanId,
    Guid FromVersionId,
    Guid ToVersionId,
    string Reason,
    Guid ActorUserId,
    DateTime OccurredAtUtc
) : IDomainEvent;
