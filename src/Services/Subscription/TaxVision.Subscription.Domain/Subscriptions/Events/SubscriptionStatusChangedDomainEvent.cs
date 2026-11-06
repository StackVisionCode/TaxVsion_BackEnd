using BuildingBlocks.Domain;

namespace TaxVision.Subscription.Domain.Subscriptions.Events;

/// <summary>
/// Motivo de un cambio anunciado de la suscripción. Los nombres coinciden con <c>SubscriptionChangeReason</c>
/// del contrato de integración, que viaja como texto: no renombrar.
/// </summary>
public enum SubscriptionStatusChangeReason
{
    TrialEnded,
    RenewalPaymentFailed,
    PaymentRecovered,
    SuspensionTimeout,
    CancellationRequested,
    CancellationScheduled,
    CancellationResumed,
    CancellationEnded,
    AdminSuspended,
    AdminReactivated,
    SelfServiceRenewed,
    RenewalOverdue,
}

/// <summary>
/// La suscripción cambió de estado (o programó/deshizo su cancelación). Un caso de uso emite uno solo: si encadena
/// transiciones (PastDue → Suspended), el aviso guarda el estado de partida y el motivo de la primera.
/// </summary>
public sealed record SubscriptionStatusChangedDomainEvent(
    Guid TenantId,
    Guid TenantSubscriptionId,
    SubscriptionStatus PreviousStatus,
    SubscriptionStatus Status,
    SubscriptionStatusChangeReason Reason,
    DateTime? AccessEndsAtUtc,
    string? FailureCode,
    Guid? ActorUserId,
    bool NotifiedByChargeFailedEmail,
    DateTime OccurredAtUtc
) : IDomainEvent;
