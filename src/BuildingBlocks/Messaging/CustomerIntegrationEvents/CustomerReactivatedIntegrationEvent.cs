namespace BuildingBlocks.Messaging.CustomerIntegrationEvents;

/// <summary>
/// Publicado cuando un customer pasa de Archived a Active.
/// Sin PII. Permite a consumidores (Auth read-model, Campaign, etc.) reactivar
/// sus proyecciones locales.
/// </summary>
public sealed record CustomerReactivatedIntegrationEvent : IntegrationEvent
{
    public required Guid CustomerId { get; init; }
    public required Guid ReactivatedByUserId { get; init; }
    public required DateTime ReactivatedAtUtc { get; init; }
}
