namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>
/// Intent publicado por Subscription cuando la suscripción base de un tenant necesita
/// cobrarse para renovarse. Subscription no conoce al payment provider — el futuro
/// servicio Billing consume este evento y responde con PaymentSucceeded/PaymentFailed.
/// </summary>
public sealed record SubscriptionRenewalDueIntegrationEvent : IntegrationEvent
{
    public required Guid TenantSubscriptionId { get; init; }
    public required string PlanCode { get; init; }
    public required DateTime PeriodStartUtc { get; init; }
    public required DateTime PeriodEndUtc { get; init; }
    public required string IdempotencyKey { get; init; }
}
