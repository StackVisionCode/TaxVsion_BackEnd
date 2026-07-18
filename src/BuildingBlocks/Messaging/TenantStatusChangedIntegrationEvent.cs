namespace BuildingBlocks.Messaging;

public sealed record TenantStatusChangedIntegrationEvent : IntegrationEvent
{
    public required Guid ChangedTenantId { get; init; }
    public required string Status { get; init; }
    public required bool IsActive { get; init; }
}
