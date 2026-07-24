namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>
/// Intent publicado por Subscription cuando la suscripción base de un tenant necesita
/// cobrarse para renovarse. Subscription no conoce al payment provider — PaymentApp
/// consume este evento y responde con SubscriptionRenewalPaymentSucceeded/FailedIntegrationEvent.
/// </summary>
public sealed record SubscriptionRenewalDueIntegrationEvent : IntegrationEvent
{
    public required Guid TenantSubscriptionId { get; init; }
    public required string PlanCode { get; init; }
    public required DateTime PeriodStartUtc { get; init; }
    public required DateTime PeriodEndUtc { get; init; }
    public required string IdempotencyKey { get; init; }

    /// <summary>Precio del plan/ciclo resuelto por Subscription en el momento de publicar
    /// el intent (fuente de verdad del pricing) — PaymentApp nunca calcula precios. Ya neteado
    /// del descuento de referido si <see cref="CodeReservationId"/> viene poblado.</summary>
    public required long AmountCents { get; init; }
    public required string Currency { get; init; }

    /// <summary>Poblados solo cuando Subscription reservó el descuento de bienvenida del
    /// referido contra Growth antes de publicar este evento (ver ActivateSubscriptionHandler,
    /// Fase 4 Referidos). PaymentApp los guarda en el SaaSPayment y, si el cobro tiene éxito,
    /// además del evento tipado de siempre publica el evento genérico
    /// PaymentIntegrationEvents.PaymentSucceededIntegrationEvent con estos mismos datos, para
    /// que el consumidor de Growth (sin cambios) confirme la reserva.</summary>
    public Guid? CodeReservationId { get; init; }
    public Guid? CodeReservationPaymentId { get; init; }
    public long? DiscountAmountCents { get; init; }
    public string? PromotionSnapshotHash { get; init; }
}
