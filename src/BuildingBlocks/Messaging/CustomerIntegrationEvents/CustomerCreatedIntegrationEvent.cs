namespace BuildingBlocks.Messaging.CustomerIntegrationEvents;

public sealed record CustomerCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid CustomerId { get; init; }
    public required string Kind { get; init; } // "Individual" | "Business"
    public required string DisplayName { get; init; }
    public required string PrimaryEmail { get; init; }
    public string? PrimaryPhone { get; init; }
    public required string Language { get; init; } // "Es" | "En"
    public required string PreferredChannel { get; init; } // "Email" | "Sms" | "Call"
    public Guid? OccupationId { get; init; }
    public required Guid CreatedByUserId { get; init; }
}
