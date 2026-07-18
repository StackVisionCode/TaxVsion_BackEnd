using BuildingBlocks.Results;

namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>
/// Cliente M2M hacia CloudStorage.Api (<c>POST /storage/files/{id}/download-url</c>) — mismo
/// patrón que <c>CloudStorageOutboundAttachmentFetcher</c> de Postmaster (D3 Compose) y
/// <c>SignatureCloudStorageClient.DownloadAsync</c>: Correspondence no descarga los bytes del
/// attachment ya subido, solo pide una URL presignada de corta duración y se la devuelve al
/// caller HTTP (<c>GetAttachmentDownloadUrlHandler</c>, Fase 8).
/// </summary>
public interface ICloudStorageClient
{
    Task<Result<CloudStorageDownloadUrl>> GetDownloadUrlAsync(
        Guid tenantId,
        Guid fileId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Fase 12 — verificación best-effort de <c>AttachFileToDraftHandler</c> contra
    /// <c>GET /storage/files/{id}</c> (mismo <c>[Authorize(Policy=CloudStoragePermissions.FileView)]</c>
    /// que ya satisface el token M2M usado por <see cref="GetDownloadUrlAsync"/> para FileDownload).
    /// CloudStorage ya filtra por tenant vía el TenantId del propio token M2M — un 200 implica
    /// "existe y pertenece a este tenant", cualquier otro resultado (404, error de red, timeout,
    /// permiso M2M no otorgado) es indistinguible acá y el caller lo trata igual: logueado, nunca
    /// bloqueante (ver comentario de <c>AttachFileToDraftHandler</c>).
    /// </summary>
    Task<Result<CloudStorageFileMetadata>> GetFileMetadataAsync(
        Guid tenantId,
        Guid fileId,
        CancellationToken ct = default
    );
}

public sealed record CloudStorageDownloadUrl(Uri DownloadUrl, DateTime ExpiresAtUtc);
