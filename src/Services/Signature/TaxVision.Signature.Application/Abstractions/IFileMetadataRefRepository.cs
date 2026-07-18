using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Application.Abstractions;

/// <summary>
/// Proyección local de archivos de CloudStorage. Alimentada por eventos
/// <c>FileAvailable</c>, <c>FileInfectedDetected</c> y <c>FileDeleted</c>. Se consulta
/// al crear una solicitud (para validar que el archivo esté disponible) y al recibir
/// <c>FileAvailable</c> para promover borradores a Ready.
/// </summary>
public interface IFileMetadataRefRepository
{
    Task<FileMetadataRef?> GetByFileIdAsync(Guid tenantId, Guid fileId, CancellationToken ct = default);

    Task AddAsync(FileMetadataRef projection, CancellationToken ct = default);
}
