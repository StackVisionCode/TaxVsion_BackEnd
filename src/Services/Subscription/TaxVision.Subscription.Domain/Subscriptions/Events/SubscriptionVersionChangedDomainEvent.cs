using BuildingBlocks.Domain;

namespace TaxVision.Subscription.Domain.Subscriptions.Events;

/// <summary>D11: la oficina pasó a otra edición de su plan (ya, o al renovar). Su foto de derechos se rehace.</summary>
public sealed record SubscriptionVersionChangedDomainEvent(
    Guid TenantId,
    Guid TenantSubscriptionId,
    Guid PlanId,
    Guid FromVersionId,
    Guid ToVersionId,
    Guid? MigrationId,
    DateTime OccurredAtUtc
) : IDomainEvent;
