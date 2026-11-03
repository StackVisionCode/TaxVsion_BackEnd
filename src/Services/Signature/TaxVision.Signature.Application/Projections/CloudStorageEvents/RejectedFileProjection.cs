using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Application.Projections.CloudStorageEvents;

/// <summary>
/// Registra en la proyección local que CloudStorage no aceptó un archivo: crea la fila si nunca hubo
/// FileAvailable, o la marca Rejected sin pisar un resultado ya conocido (ver <see cref="FileMetadataRef.MarkRejected"/>).
/// </summary>
internal static class RejectedFileProjection
{
    public static async Task RecordAsync(
        IFileMetadataRefRepository repository,
        Guid tenantId,
        Guid fileId,
        string reason,
        CancellationToken ct
    )
    {
        var existing = await repository.GetByFileIdAsync(tenantId, fileId, ct);
        if (existing is null)
            await repository.AddAsync(FileMetadataRef.ForRejected(tenantId, fileId, reason), ct);
        else
            existing.MarkRejected(reason);
    }

    public static string CorrelationOf(string? correlationId, Guid eventId) =>
        string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId;
}
