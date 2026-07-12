namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// Todos los firmantes firmaron. Este evento dispara la fase de sellado
/// (Fase 4 en el diseño): el worker genera el PDF sellado con el certificado y
/// el bloque de audit, y publica <c>SignatureRequestSealedIntegrationEvent</c>.
/// </summary>
public sealed record SignatureRequestCompletedIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required DateTime CompletedAtUtc { get; init; }
    public required Guid OriginalFileId { get; init; }
    public required string DocumentHashPre { get; init; }
    public required IReadOnlyList<Guid> SignerIds { get; init; }
    public required bool GenerateCertificate { get; init; }
}
