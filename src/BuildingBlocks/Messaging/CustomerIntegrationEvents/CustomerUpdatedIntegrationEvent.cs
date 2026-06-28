namespace BuildingBlocks.Messaging.CustomerIntegrationEvents;

public sealed record CustomerUpdatedIntegrationEvent : IntegrationEvent
{
    public required Guid CustomerId { get; init; }
    public required string DisplayName { get; init; }
    public required string PrimaryEmail { get; init; }
    public string? PrimaryPhone { get; init; }
    public required string Language { get; init; }
    public required string PreferredChannel { get; init; }
    public Guid? OccupationId { get; init; }
    public required Guid ModifiedByUserId { get; init; }
}
