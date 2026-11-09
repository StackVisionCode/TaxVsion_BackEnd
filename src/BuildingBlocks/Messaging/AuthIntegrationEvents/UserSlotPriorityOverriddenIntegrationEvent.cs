using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

[MessageIdentity("BuildingBlocks.Messaging.AuthIntegrationEvents.UserSlotPriorityOverriddenIntegrationEvent")]
public sealed record UserSlotPriorityOverriddenIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
    public DateTime? PreviousAt { get; init; }
    public DateTime? NewAt { get; init; }
    public required Guid ActorAdminId { get; init; }
    public required string Reason { get; init; }
}
