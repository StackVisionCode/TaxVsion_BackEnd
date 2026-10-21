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
    SuspensionTimeout,
    CancellationRequested,

    /// <summary>El tenant canceló al fin del período: sigue activo y pagado hasta esa fecha.</summary>
    CancellationScheduled,

    /// <summary>Deshizo esa cancelación antes de que llegara el fin del período.</summary>
    CancellationResumed,

    /// <summary>Recordatorio: se acerca el fin del acceso de una cancelación programada. No es una
    /// transición — viaja por el mismo canal para reusar la resolución de destinatario y el envío.</summary>
    AccessEnding,

    /// <summary>Recordatorio pre-vencimiento (7 o 1 día). No es una transición — viaja por el canal
    /// de emails para reusar la resolución de destinatario; la suscripción sigue Active.</summary>
    RenewalUpcoming,
    CancellationEnded,
    AdminSuspended,
    AdminReactivated,
    SelfServiceRenewed,

    /// <summary>
    /// A6 — **no es una transición**: es el job de anti-entropía re-anunciando el estado ACTUAL para
    /// que los servicios que guardan su consecuencia (Auth y su <c>BillingAccessBlocked</c>) converjan
    /// tras un evento perdido.
    ///
    /// Los consumidores que producen efectos hacia fuera —correos, métricas de transición— DEBEN
    /// ignorarlo: re-anunciar un <c>Expired</c> a diario le mandaría al tenant el correo de
    /// "tu suscripción venció" todos los días, y contaría una transición que no ocurrió. Los que solo
    /// derivan estado (el corte de acceso) sí deben procesarlo: es justo para ellos.
    /// </summary>
    Reconciliation,

    /// <summary>Cobro recurrente off-session exitoso. No es una transición: la suscripción
    /// sigue Active. Viaja por este canal para reusar la resolución de destinatario y el envío.</summary>
    RecurringChargeSucceeded,

    /// <summary>Cobro recurrente off-session fallido. No es una transición por sí mismo:
    /// la transición a PastDue/Suspended la publica el flujo de dunning después.</summary>
    RecurringChargeFailed,

    /// <summary>El user cambió su modo de facturación (Auto↔Manual). No es una transición
    /// del estado de la suscripción.</summary>
    BillingModeChanged,

    /// <summary>El user eligió Automatic por primera vez y guardó un método; se emitió un
    /// mandate. Acompaña a BillingModeChanged cuando el cambio es Manual→Automatic.</summary>
    MandateCreated,

    /// <summary>El período venció hace días y el cobro de la renovación nunca salió: se suspende.</summary>
    RenewalOverdue,
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

    /// <summary>Estado nuevo (<c>SubscriptionStatus.ToString()</c>): Active/PastDue/Suspended/Expired/…</summary>
    public required string Status { get; init; }

    /// <summary>Estado inmediatamente anterior a la transición.</summary>
    public required string PreviousStatus { get; init; }

    /// <summary>Motivo (<see cref="SubscriptionChangeReason"/> stringificado).</summary>
    public required string Reason { get; init; }

    /// <summary>Hasta cuándo llega el acceso ya pagado. Lo llena la cancelación programada, para que el
    /// correo pueda decir la fecha exacta en la que se termina.</summary>
    public DateTime? AccessEndsAtUtc { get; init; }

    /// <summary>Código de fallo del proveedor, cuando la transición la dispara un pago fallido.</summary>
    public string? FailureCode { get; init; }

    /// <summary>Usuario que originó la transición (admin/self-service); <c>null</c> si la disparó un job.</summary>
    public Guid? ActorUserId { get; init; }

    /// <summary>Importe cobrado/intentado ya formateado (recurring_charge*/mandate_created).</summary>
    public string? Amount { get; init; }

    /// <summary>Últimos 4 del método guardado.</summary>
    public string? CardLast4 { get; init; }

    /// <summary>Marca del método guardado.</summary>
    public string? CardBrand { get; init; }

    /// <summary>URL del recibo del proveedor (recurring_charge_succeeded).</summary>
    public string? ReceiptUrl { get; init; }

    /// <summary>Modo nuevo cuando cambió la preferencia (Automatic/Manual).</summary>
    public string? NewMode { get; init; }

    /// <summary>Cadencia del mandate (month/year).</summary>
    public string? Cadence { get; init; }

    /// <summary>Fecha formateada del cobro.</summary>
    public string? DateCharged { get; init; }

    /// <summary>Fecha formateada del próximo cobro.</summary>
    public string? NextChargeDate { get; init; }
}
