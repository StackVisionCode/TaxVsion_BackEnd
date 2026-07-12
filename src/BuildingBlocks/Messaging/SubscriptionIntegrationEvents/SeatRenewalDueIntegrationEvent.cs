namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>Intent publicado por Subscription cuando un seat necesita cobrarse para
/// renovarse. Independiente de SubscriptionRenewalDue — un seat puede vencer sin que la
/// suscripción base venza, y viceversa.</summary>
public sealed record SeatRenewalDueIntegrationEvent : IntegrationEvent
{
    public required Guid SeatId { get; init; }
    public required DateTime PeriodStartUtc { get; init; }
    public required DateTime PeriodEndUtc { get; init; }
    public required string IdempotencyKey { get; init; }
}
