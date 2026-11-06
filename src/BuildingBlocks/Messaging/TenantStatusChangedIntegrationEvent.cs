using Wolverine.Attributes;

namespace BuildingBlocks.Messaging;

[MessageIdentity("BuildingBlocks.Messaging.TenantStatusChangedIntegrationEvent")]
public sealed record TenantStatusChangedIntegrationEvent : IntegrationEvent
{
    public required Guid ChangedTenantId { get; init; }
    public required string Status { get; init; }
    public required bool IsActive { get; init; }
}
