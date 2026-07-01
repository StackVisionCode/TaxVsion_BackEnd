namespace BuildingBlocks.Messaging;

public sealed record SeatPaymentFailedIntegrationEvent : IntegrationEvent
{
    public Guid SeatSubscriptionId { get; init; }
    public string Reason { get; init; } = string.Empty;
}
