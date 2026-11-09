using BuildingBlocks.Domain;

namespace TaxVision.CloudStorage.Domain.Files.Events;

/// <summary>
/// El archivo quedó disponible (escaneo limpio o revisión aprobada). Reinstalar tras un DMCA no lo levanta: ese camino
/// anuncia su propio evento y nunca publicó <c>FileAvailable</c>.
/// </summary>
/// <param name="PortalCustomerId">Cliente al que avisar que subió un documento; null si no corresponde.</param>
public sealed record FileBecameAvailableDomainEvent(
    Guid TenantId,
    Guid FileId,
    string ObjectKey,
    string ContentType,
    long SizeBytes,
    string ChecksumSha256,
    Guid CreatedBy,
    string OwnerType,
    Guid? OwnerId,
    Guid? FolderId,
    string FolderType,
    string OriginalName,
    int? TaxYear,
    string? CorrelationId,
    Guid? PortalCustomerId
) : IDomainEvent;

public enum FileQuarantineKind
{
    Infected,
    BlockedByPolicy,
    PendingReview,
}

/// <summary>El archivo quedó fuera de circulación: infectado, bloqueado por política o a la espera de revisión.</summary>
public sealed record FileQuarantinedDomainEvent(
    Guid TenantId,
    Guid FileId,
    FileQuarantineKind Kind,
    string ObjectKey,
    string Reason,
    Guid CreatedBy,
    string OwnerType,
    Guid? OwnerId,
    string FolderType,
    string? CorrelationId
) : IDomainEvent;

/// <summary>El escaneo no pudo terminar con un veredicto: tipo no permitido, sin objeto o sin intentos.</summary>
/// <param name="Reason">Código estable para quien consume el aviso; el detalle queda en el reporte del archivo.</param>
public sealed record FileScanFailedDomainEvent(
    Guid TenantId,
    Guid FileId,
    string ObjectKey,
    string Reason,
    Guid CreatedBy,
    string OwnerType,
    Guid? OwnerId,
    string FolderType,
    string? CorrelationId
) : IDomainEvent;
