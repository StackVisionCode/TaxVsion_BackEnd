using BuildingBlocks.Results;

namespace TaxVision.Notes.Application.Notes.Abstractions;

/// <summary>URL presignada de un archivo, emitida por CloudStorage.</summary>
public sealed record NoteFileDownloadUrl(Uri DownloadUrl, DateTime ExpiresAtUtc);

/// <summary>
/// Pide a CloudStorage la URL de un adjunto con el token de servicio de la oficina. Solo se llama después de aplicar
/// la regla de la nota: para una persona, CloudStorage ya no entrega un adjunto de nota por id.
/// </summary>
public interface INoteFileDownloadClient
{
    Task<Result<NoteFileDownloadUrl>> GetDownloadUrlAsync(
        Guid tenantId,
        Guid fileId,
        bool forPreview,
        CancellationToken ct = default
    );
}
