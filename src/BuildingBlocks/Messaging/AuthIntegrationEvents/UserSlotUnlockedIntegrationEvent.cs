using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

[MessageIdentity("BuildingBlocks.Messaging.AuthIntegrationEvents.UserSlotUnlockedIntegrationEvent")]
public sealed record UserSlotUnlockedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
    public string? UserFullName { get; init; }
    public string? UserEmail { get; init; }
    public string? PrimaryAdminEmail { get; init; }
    public string? TenantName { get; init; }
    public string? TenantSubdomain { get; init; }
}
