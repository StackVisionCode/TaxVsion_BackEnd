namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>Publicado por Subscription al aplicar un upgrade/downgrade de plan.</summary>
public sealed record SubscriptionPlanChangedIntegrationEvent : IntegrationEvent
{
    public required Guid SubscribedTenantId { get; init; }
    public required string PlanCode { get; init; }
    public required int MaxUsers { get; init; }
    public required int MaxPendingInvitations { get; init; }
    public long StorageQuotaBytes { get; init; }
    public string[] EnabledModules { get; init; } = [];
}
