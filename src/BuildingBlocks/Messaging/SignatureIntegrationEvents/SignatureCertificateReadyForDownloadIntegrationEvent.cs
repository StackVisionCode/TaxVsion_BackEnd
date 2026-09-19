namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// El Certificate of Completion ya está DISPONIBLE en CloudStorage (pasó el scan) y tiene un
/// share-link de descarga emitido. Signature lo publica desde el consumer de
/// <c>FileAvailableIntegrationEvent</c> cuando el archivo disponible coincide con el
/// <c>CertificateFileId</c> de una request Y ésta pidió entregarlo (<c>SendCertificateToSigners</c>).
/// Notification lo consume para entregar el certificado a cada firmante por su canal (email/SMS).
///
/// <para>
/// Es un evento aparte del documento firmado porque el certificado es OTRO archivo con su propio
/// scan: dispararse con su FileAvailable evita la carrera de "aún no está listo" y respeta el flag
/// de entrega independiente.
/// </para>
/// </summary>
public sealed record SignatureCertificateReadyForDownloadIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid CertificateFileId { get; init; }
    public required DateTime CompletedAtUtc { get; init; }

    /// <summary>Token del share-link público del certificado. Null si no se pudo emitir.</summary>
    public string? ShareToken { get; init; }

    /// <summary>Snapshot de contacto de cada firmante — destinatarios, sin lookup síncrono.</summary>
    public required IReadOnlyList<SignerContactSnapshot> Signers { get; init; }
}
