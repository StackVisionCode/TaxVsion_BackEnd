namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>
/// Publicado por Subscription cada vez que se recalcula el entitlement snapshot de un
/// tenant. CloudStorage, Notification, Signature, Communication, Planner y Email lo
/// consumen para saber que deben refrescar los límites/flags que aplican localmente.
/// </summary>
public sealed record TenantEntitlementsChangedIntegrationEvent : IntegrationEvent
{
    public required long RevisionNumber { get; init; }
    public required string[] ChangedKeys { get; init; }
    public required string PlanCode { get; init; }
    public required string SubscriptionStatus { get; init; }
}
