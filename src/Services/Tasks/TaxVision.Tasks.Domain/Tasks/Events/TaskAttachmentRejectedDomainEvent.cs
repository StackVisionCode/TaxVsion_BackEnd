using BuildingBlocks.Domain;

namespace TaxVision.Tasks.Domain.Tasks.Events;

/// <summary>
/// El escaneo rechazó un archivo ya adjunto. Lleva a quien lo adjuntó porque el aviso puede llegar
/// después de que la tarea se cerró, y para entonces nadie está mirando esa tarea.
/// </summary>
public sealed record TaskAttachmentRejectedDomainEvent(
    Guid TaskId,
    Guid TenantId,
    Guid AttachmentId,
    Guid FileId,
    string DisplayName,
    string Reason,
    Guid AttachedByUserId,
    DateTime RejectedAtUtc
) : IDomainEvent;
