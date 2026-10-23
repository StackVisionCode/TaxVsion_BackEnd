namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

public sealed record ClientFormActorRef
{
    public required string Kind { get; init; }
    public Guid? PortalUserId { get; init; }
    public Guid? ParticipantId { get; init; }
}
