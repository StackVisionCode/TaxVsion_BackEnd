using BuildingBlocks.Domain;

namespace TaxVision.Subscription.Domain.Subscriptions.Events;

/// <summary>D11: se agendó el paso de la oficina a otra edición de su plan. Lleva el aviso al cliente.</summary>
public sealed record SubscriptionVersionChangeScheduledDomainEvent(
    Guid TenantId,
    Guid TenantSubscriptionId,
    Guid PlanId,
    string PlanCode,
    Guid FromVersionId,
    Guid ToVersionId,
    DateTime EffectiveAtUtc,
    Guid MigrationId,
    string ChangeSummary,
    SubscriptionStatus Status,
    Guid ActorUserId,
    DateTime OccurredAtUtc
) : IDomainEvent;
