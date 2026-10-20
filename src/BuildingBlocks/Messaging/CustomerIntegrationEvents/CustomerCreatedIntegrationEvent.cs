namespace BuildingBlocks.Messaging.CustomerIntegrationEvents;

public sealed record CustomerCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid CustomerId { get; init; }
    public required string Kind { get; init; } // "Individual" | "Business"
    public required string DisplayName { get; init; }
    public required string PrimaryEmail { get; init; }
    public string? PrimaryPhone { get; init; }
    public required string Language { get; init; } // "Es" | "En"

    /// <summary>
    /// Política de contacto por canal (T7.2). Aditivo — default true/true para consumidores
    /// que lo ignoran. Notification lo proyecta a <c>CustomerContactPolicyProjection</c> y los
    /// consumers downstream deciden fan-out por canal.
    /// </summary>
    public bool EmailEnabled { get; init; } = true;
    public bool SmsEnabled { get; init; } = true;
    public Guid? OccupationId { get; init; }
    public required Guid CreatedByUserId { get; init; }
}
