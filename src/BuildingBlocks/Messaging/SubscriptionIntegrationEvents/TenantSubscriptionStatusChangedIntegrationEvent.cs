namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>
/// Motivo de una transición de estado de la suscripción base — acompaña a
/// <see cref="TenantSubscriptionStatusChangedIntegrationEvent"/> para que los consumidores (corte de
/// acceso, notificaciones, UX) sepan por qué cambió sin re-derivarlo del par (from, to).
/// </summary>
public enum SubscriptionChangeReason
{
    Unknown = 0,
    TrialConverted,
    TrialEnded,
    RenewalPaymentFailed,
    PaymentRecovered,
    GraceEntered,
    GraceExpired,
    SuspensionTimeout,
    CancellationRequested,

    /// <summary>El tenant canceló al fin del período: sigue activo y pagado hasta esa fecha.</summary>
    CancellationScheduled,

    /// <summary>Deshizo esa cancelación antes de que llegara el fin del período.</summary>
    CancellationResumed,

    /// <summary>Recordatorio: se acerca el fin del acceso de una cancelación programada. No es una
    /// transición — viaja por el mismo canal para reusar la resolución de destinatario y el envío.</summary>
    AccessEnding,
    CancellationEnded,
    AdminSuspended,
    AdminReactivated,
    SelfServiceRenewed,
}

/// <summary>
/// Ciclo de vida de suscripción (plan de Expiración/Dunning, Fase 0) — se publica en CADA transición de
/// estado del agregado <c>TenantSubscription</c>, ADEMÁS del recálculo de entitlements existente. Es la
/// única fuente que alimentará: (a) el corte/reactivación de acceso en Auth (staff + clientes), (b) las
/// notificaciones/dunning, (c) proyecciones de UX. Aditivo: sin consumidores todavía, no cambia el
/// comportamiento actual. <see cref="IntegrationEvent.TenantId"/> y <see cref="IntegrationEvent.CorrelationId"/>
/// vienen de la base.
/// </summary>
public sealed record TenantSubscriptionStatusChangedIntegrationEvent : IntegrationEvent
{
    public required Guid TenantSubscriptionId { get; init; }

    /// <summary>Estado nuevo (<c>SubscriptionStatus.ToString()</c>): Active/PastDue/GracePeriod/Suspended/Expired/…</summary>
    public required string Status { get; init; }

    /// <summary>Estado inmediatamente anterior a la transición.</summary>
    public required string PreviousStatus { get; init; }

    /// <summary>Motivo (<see cref="SubscriptionChangeReason"/> stringificado).</summary>
    public required string Reason { get; init; }

    /// <summary>Fin de la ventana de gracia, cuando el estado nuevo es <c>GracePeriod</c>.</summary>
    public DateTime? GracePeriodEndsAtUtc { get; init; }

    /// <summary>Hasta cuándo llega el acceso ya pagado. Lo llena la cancelación programada, para que el
    /// correo pueda decir la fecha exacta en la que se termina.</summary>
    public DateTime? AccessEndsAtUtc { get; init; }

    /// <summary>Código de fallo del proveedor, cuando la transición la dispara un pago fallido.</summary>
    public string? FailureCode { get; init; }

    /// <summary>Usuario que originó la transición (admin/self-service); <c>null</c> si la disparó un job.</summary>
    public Guid? ActorUserId { get; init; }
}
