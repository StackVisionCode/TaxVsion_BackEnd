namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>Publicado por Subscription cuando se cancela un add-on de un tenant.</summary>
public sealed record AddOnCancelledIntegrationEvent : IntegrationEvent
{
    public required Guid TenantAddOnId { get; init; }
    public required string AddOnCode { get; init; }
    public required string Reason { get; init; }
}
