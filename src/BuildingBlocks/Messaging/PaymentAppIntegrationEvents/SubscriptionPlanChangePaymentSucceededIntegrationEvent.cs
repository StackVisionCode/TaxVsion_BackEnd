namespace BuildingBlocks.Messaging.PaymentAppIntegrationEvents;

/// <summary>
/// Publicado por PaymentApp cuando el cargo prorrateado de un upgrade de plan (originado por
/// <see cref="SubscriptionIntegrationEvents.SubscriptionPlanChangeDueIntegrationEvent"/>) se
/// confirma exitoso. Subscription lo consume para recién ahí aplicar el cambio de plan.
/// Sin TenantSubscriptionId a propósito: <see cref="PlanChangeRequestId"/> (más el TenantId
/// heredado de <see cref="IntegrationEvent"/>) alcanza para ubicar el request — un tenant
/// tiene una sola suscripción base.
/// </summary>
public sealed record SubscriptionPlanChangePaymentSucceededIntegrationEvent : IntegrationEvent
{
    public required Guid PlanChangeRequestId { get; init; }
    public required Guid SaaSPaymentId { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string ExternalPaymentReference { get; init; }
    public required DateTime PaidAtUtc { get; init; }
}
