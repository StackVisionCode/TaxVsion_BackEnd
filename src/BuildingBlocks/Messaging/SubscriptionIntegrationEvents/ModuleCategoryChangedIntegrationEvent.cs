namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

public sealed record ModuleCategoryChangedIntegrationEvent : IntegrationEvent
{
    public required string ModuleCode { get; init; }
    public required string OldCategory { get; init; }
    public required string NewCategory { get; init; }
    public bool IsSellableAsAddon { get; init; }
}
