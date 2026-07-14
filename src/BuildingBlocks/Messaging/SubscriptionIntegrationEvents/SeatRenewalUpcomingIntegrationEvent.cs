namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>Publicado por Subscription cuando faltan 7, 3 o 1 día(s) para que un seat se
/// renueve. Independiente de SubscriptionRenewalUpcoming.</summary>
public sealed record SeatRenewalUpcomingIntegrationEvent : IntegrationEvent
{
    public required Guid SeatId { get; init; }
    public required DateTime DueAtUtc { get; init; }
    public required int DaysUntilDue { get; init; }
    public Guid? CurrentUserId { get; init; }
}
