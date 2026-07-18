namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// El staff creó una solicitud de firma en estado Draft. Todavía no se envía a los
/// firmantes; se emite para que otros microservicios inicien el ciclo de vida (crear
/// tareas de seguimiento en Planner, agregar entrada al historial del cliente, etc.).
/// </summary>
public sealed record SignatureRequestCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required string Title { get; init; }
    public required string Category { get; init; } // SignatureCategory.ToString()
    public required Guid OriginalFileId { get; init; }
    public required int TokenExpirationHours { get; init; }
    public required bool RequiresSequentialSigning { get; init; }
    public required int SignerCount { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }
}
