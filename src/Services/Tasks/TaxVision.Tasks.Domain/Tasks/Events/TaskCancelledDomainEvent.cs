using BuildingBlocks.Domain;

namespace TaxVision.Tasks.Domain.Tasks.Events;

/// <summary>
/// Cancelar también desbloquea: para las dependencias «cancelada» y «completada» son lo mismo, y
/// escuchar sólo la completada dejaría a la sucesora bloqueada para siempre.
/// </summary>
public sealed record TaskCancelledDomainEvent(
    Guid TaskId,
    Guid TenantId,
    Guid? ParentTaskId,
    Guid? SeriesId,
    string Reason,
    Guid CancelledByUserId,
    DateTime CancelledAtUtc
) : IDomainEvent;
