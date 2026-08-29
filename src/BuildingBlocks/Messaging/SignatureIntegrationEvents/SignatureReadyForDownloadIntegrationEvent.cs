namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// El documento sellado ya está DISPONIBLE en CloudStorage (pasó el scan) y tiene un share-link de
/// descarga emitido. Signature lo publica desde el consumer de <c>FileAvailableIntegrationEvent</c>
/// cuando el archivo disponible es el sellado de una request — no antes, porque crear el share-link
/// exige que el archivo esté <c>Available</c>. Notification lo consume para mandar el correo de firma
/// completada con el botón "descargar documento firmado".
/// </summary>
public sealed record SignatureReadyForDownloadIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid SealedFileId { get; init; }
    public required DateTime CompletedAtUtc { get; init; }

    /// <summary>
    /// Token del share-link público de CloudStorage. Null si no se pudo emitir: el correo igual sale,
    /// pero sin botón de descarga.
    /// </summary>
    public string? ShareToken { get; init; }

    /// <summary>Snapshot de contacto de cada firmante — destinatarios del correo, sin lookup síncrono.</summary>
    public required IReadOnlyList<SignerContactSnapshot> Signers { get; init; }
}
