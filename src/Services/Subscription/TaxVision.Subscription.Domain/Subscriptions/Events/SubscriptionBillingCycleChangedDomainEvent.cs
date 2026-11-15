using BuildingBlocks.Domain;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Subscriptions.Events;

/// <summary>
/// La suscripción cambió de ciclo de facturación. Pasar a anual se anuncia al pagarse (con lo cobrado); pasar a mensual,
/// al agendarse (sin cobro, desde el fin del período). Lleva el aviso al cliente.
/// </summary>
public sealed record SubscriptionBillingCycleChangedDomainEvent(
    Guid TenantId,
    Guid TenantSubscriptionId,
    string PlanCode,
    BillingCycle NewBillingCycle,
    long? ChargedAmountCents,
    string? Currency,
    DateTime EffectiveAtUtc,
    DateTime NextRenewalAtUtc,
    SubscriptionStatus Status,
    Guid ActorUserId,
    DateTime OccurredAtUtc
) : IDomainEvent;
