namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

public sealed record ClientFormParticipant
{
    public required string Kind { get; init; }
    public Guid? CustomerId { get; init; }
    public Guid? ParticipantId { get; init; }
}
