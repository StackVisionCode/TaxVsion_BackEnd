namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// Todos los firmantes firmaron. Este evento dispara la fase de sellado
/// (Fase 4 en el diseño): el worker genera el PDF sellado con el certificado y
/// el bloque de audit, y publica un evento por documento sellado y otro al completar la request.
/// </summary>
public sealed record SignatureRequestCompletedIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required DateTime CompletedAtUtc { get; init; }
    public required IReadOnlyList<DocumentHashDescriptor> Documents { get; init; }
    public required IReadOnlyList<Guid> SignerIds { get; init; }
    public required bool GenerateCertificate { get; init; }

    /// <summary>Snapshot de contacto de cada firmante — para notificaciones de confirmación sin lookup síncrono.</summary>
    public required IReadOnlyList<SignerContactSnapshot> Signers { get; init; }
}
