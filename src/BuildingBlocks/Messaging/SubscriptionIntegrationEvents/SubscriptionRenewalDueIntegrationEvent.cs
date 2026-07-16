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
    /// el intent (fuente de verdad del pricing) — PaymentApp nunca calcula precios.</summary>
    public required long AmountCents { get; init; }
    public required string Currency { get; init; }
}
