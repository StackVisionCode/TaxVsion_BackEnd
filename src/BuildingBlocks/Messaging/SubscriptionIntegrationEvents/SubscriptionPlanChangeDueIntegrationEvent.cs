namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>
/// Intent publicado por Subscription cuando un upgrade de plan tiene un cobro pendiente.
/// <see cref="TargetPlanPrice"/> es SIEMPRE el precio COMPLETO del plan destino (en
/// centavos) — nunca una diferencia prorrateada, nunca un cálculo por días restantes.
/// Subscription ya calculó y es la fuente de verdad del monto — PaymentApp solo cobra y
/// responde con SubscriptionPlanChangePaymentSucceeded/FailedIntegrationEvent. A diferencia
/// de <see cref="SubscriptionRenewalDueIntegrationEvent"/>, el plan de la suscripción NO
/// cambió todavía cuando se publica este evento — recién cambia si el cobro se confirma.
/// </summary>
public sealed record SubscriptionPlanChangeDueIntegrationEvent : IntegrationEvent
{
    public required Guid TenantSubscriptionId { get; init; }
    public required Guid PlanChangeRequestId { get; init; }
    public required Guid TargetPlanId { get; init; }
    public required string IdempotencyKey { get; init; }

    /// <summary>Precio completo del plan destino, en centavos — no una diferencia.</summary>
    public required long TargetPlanPrice { get; init; }
    public required string Currency { get; init; }

    /// <summary>Fase 1B — quien pidió el upgrade (<c>PlanChangeRequest.RequestedByUserId</c>).
    /// A diferencia de SubscriptionRenewalDue/SeatRenewalDue/AddOnRenewalDue (disparados por un
    /// job de facturación, sin usuario), un upgrade siempre lo inicia un usuario interactivo.</summary>
    public required Guid RequestedByUserId { get; init; }
}
