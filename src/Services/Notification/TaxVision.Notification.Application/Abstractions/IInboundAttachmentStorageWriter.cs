using BuildingBlocks.Results;

namespace TaxVision.Notification.Application.Abstractions;

/// <summary>
/// Fase D3 — sube un adjunto de un email sincronizado por IMAP directo a MinIO
/// (credenciales propias, IAM scoped) y publica <c>SaveFileRequestedIntegrationEvent</c>
/// para que CloudStorage lo registre y escanee. Reemplaza el HTTP initiate/PUT/complete
/// que sigue usando <see cref="ICloudStorageClient"/> para los assets de plantillas/layouts
/// (esos se dejan intactos: van en contexto de request con el JWT del usuario, no
/// background — mismo criterio que dejo intacto DownloadAsync en D1/D2).
/// </summary>
public sealed record InboundAttachmentUpload(
    byte[] Content,
    string OriginalName,
    string ContentType,
    Guid MessageId,
    int? TaxYear
);

public interface IInboundAttachmentStorageWriter
{
    Task<Result<Guid>> SaveAsync(InboundAttachmentUpload upload, Guid tenantId, CancellationToken ct = default);
}
