namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// El sealing terminó con éxito: existe un PDF sellado en CloudStorage y — cuando la
/// solicitud lo pidió — un Certificate of Completion aparte. Comunica esos IDs al
/// resto del ecosistema (dashboard staff, portal cliente, Planner para marcar tareas
/// como cerradas). El PDF sellado es la copia canónica final.
/// </summary>
public sealed record SignatureRequestSealedIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required Guid SealedFileId { get; init; }
    public required string DocumentHashPost { get; init; }
    public Guid? CertificateFileId { get; init; }
    public required DateTime SealedAtUtc { get; init; }
}
