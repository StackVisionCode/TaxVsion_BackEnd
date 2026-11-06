using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>Publicado por el sweeper cuando un upgrade por checkout vence sin pago.</summary>
[MessageIdentity("BuildingBlocks.Messaging.SubscriptionIntegrationEvents.PlanChangeUpgradeExpiredIntegrationEvent")]
public sealed record PlanChangeUpgradeExpiredIntegrationEvent : IntegrationEvent
{
    public required Guid PlanChangeRequestId { get; init; }
    public Guid? SaaSPaymentId { get; init; }
}
