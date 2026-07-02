namespace BuildingBlocks.Messaging.CustomerIntegrationEvents;

public sealed record CustomerPortalInvitationRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid CustomerId { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public required Guid RequestedByUserId { get; init; }
}
