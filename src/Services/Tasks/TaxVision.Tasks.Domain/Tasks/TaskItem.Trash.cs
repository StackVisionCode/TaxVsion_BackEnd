using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using TaxVision.Tasks.Domain.Tasks.Events;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Domain.Tasks;

/// <summary>Papelera: borrar manda la tarea y su rama; Restore la devuelve tal como entró.</summary>
public sealed partial class TaskItem
{
    /// <summary>A la papelera con su rama (<paramref name="trashRootId"/>). La rama la recorre el servicio de jerarquía.</summary>
    public Result Delete(Guid byUserId, DateTime nowUtc, Guid trashRootId)
    {
        if (IsInTrash)
            return Result.Success();
        DeletedAtUtc = nowUtc;
        DeletedByUserId = byUserId;
        TrashRootId = trashRootId;
        AddDomainEvent(new TaskDeletedDomainEvent(Id, TenantId, ParentTaskId, IsClosed, byUserId, nowUtc));
        return Result.Success();
    }

    /// <summary>Sale de la papelera tal como entró: mismo estado, subtareas, archivos y tiempo.</summary>
    public Result Restore(Guid byUserId, DateTime nowUtc)
    {
        if (!IsInTrash)
            return Result.Failure(TaskErrors.Trash.NotInTrash);
        DeletedAtUtc = null;
        DeletedByUserId = null;
        TrashRootId = null;
        AddDomainEvent(new TaskRestoredDomainEvent(Id, TenantId, byUserId, nowUtc));
        return Result.Success();
    }
}
