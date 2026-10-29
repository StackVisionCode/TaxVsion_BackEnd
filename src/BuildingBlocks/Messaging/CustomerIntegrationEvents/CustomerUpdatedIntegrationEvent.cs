namespace BuildingBlocks.Messaging.CustomerIntegrationEvents;

public sealed record CustomerUpdatedIntegrationEvent : IntegrationEvent
{
    public required Guid CustomerId { get; init; }
    public required string DisplayName { get; init; }
    public required string PrimaryEmail { get; init; }
    public string? PrimaryPhone { get; init; }
    public required string Language { get; init; }

    /// <summary>
    /// Política de contacto por canal (T7.2). Aditivo — default true/true para consumidores
    /// que lo ignoran.
    /// </summary>
    public bool EmailEnabled { get; init; } = true;
    public bool SmsEnabled { get; init; } = true;
    public Guid? OccupationId { get; init; }

    /// <summary>Foto de perfil vigente (null = sin foto). Aditivo; se sirve en <c>/storage/avatars/{id}</c>.</summary>
    public Guid? AvatarFileId { get; init; }
    public required Guid ModifiedByUserId { get; init; }
}
