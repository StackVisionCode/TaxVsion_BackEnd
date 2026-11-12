using BuildingBlocks.Domain;

namespace TaxVision.Subscription.Domain.Plans.Events;

/// <summary>Se creó un plan: sus filas de rate limit se crean copiando el perfil por defecto (D12).</summary>
public sealed record PlanCreatedDomainEvent(Guid PlanId, string PlanCode, Guid ActorUserId, DateTime OccurredAtUtc)
    : IDomainEvent;
