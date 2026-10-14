namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>Publicado por el sweeper cuando un upgrade por checkout vence sin pago.</summary>
public sealed record PlanChangeUpgradeExpiredIntegrationEvent : IntegrationEvent
{
    public required Guid PlanChangeRequestId { get; init; }
    public Guid? SaaSPaymentId { get; init; }
}
