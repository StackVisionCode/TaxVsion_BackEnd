namespace BuildingBlocks.Messaging.PaymentAppIntegrationEvents;

/// <summary>
/// Publicado por PaymentApp cuando el cargo prorrateado de un upgrade de plan (originado por
/// <see cref="SubscriptionIntegrationEvents.SubscriptionPlanChangeDueIntegrationEvent"/>) falla.
/// Sin WillRetry/NextRetryAtUtc a propósito: es un cargo interactivo iniciado por el usuario,
/// no dunning en background — un solo intento, y el request queda en PaymentFailed. Subscription
/// no toca el plan porque nunca lo había cambiado. Sin TenantSubscriptionId a propósito, igual
/// que <see cref="SubscriptionPlanChangePaymentSucceededIntegrationEvent"/>.
/// </summary>
public sealed record SubscriptionPlanChangePaymentFailedIntegrationEvent : IntegrationEvent
{
    public required Guid PlanChangeRequestId { get; init; }
    public required Guid SaaSPaymentId { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string FailureCode { get; init; }
    public required string FailureReason { get; init; }
}
