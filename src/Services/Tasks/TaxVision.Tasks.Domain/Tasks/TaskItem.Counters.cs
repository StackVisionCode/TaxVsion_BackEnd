using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using TaxVision.Tasks.Domain.Tasks.Events;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Domain.Tasks;

/// <summary>Contadores de esperas y subtareas que llevan los servicios de la aplicación, y su reconciliación.</summary>
public sealed partial class TaskItem
{
    internal void RegisterBlockerAdded() => OpenBlockerCount++;

    /// <summary>Deja constancia de que la tarea empezó o dejó de esperar a otra: lo recoge el historial.</summary>
    public void RecordDependencyChange(Guid dependsOnTaskId, bool added, Guid byUserId, DateTime nowUtc) =>
        AddDomainEvent(new TaskDependencyChangedDomainEvent(Id, TenantId, dependsOnTaskId, added, byUserId, nowUtc));

    /// <summary>Nunca baja de 0: un evento reprocesado no puede desbloquear una tarea que sigue bloqueada.</summary>
    internal void RegisterBlockerResolved(DateTime nowUtc)
    {
        if (OpenBlockerCount == 0)
            return;

        OpenBlockerCount--;
        if (OpenBlockerCount == 0)
            AddDomainEvent(new TaskUnblockedDomainEvent(Id, TenantId, AssigneeUserId, nowUtc));
    }

    /// <summary>Un bloqueador ya resuelto se reabrió y vuelve a bloquear.</summary>
    internal void RegisterBlockerReopened() => OpenBlockerCount++;

    internal void RegisterSubtaskOpened() => OpenSubtaskCount++;

    internal void RegisterSubtaskClosed()
    {
        if (OpenSubtaskCount > 0)
            OpenSubtaskCount--;
    }

    internal void RegisterSubtaskReopened() => OpenSubtaskCount++;

    internal void RegisterSubtaskRemoved()
    {
        if (SubtaskCount > 0)
            SubtaskCount--;
    }

    /// <summary>Una subtarea volvió de la papelera.</summary>
    internal void RegisterSubtaskRestored(bool open)
    {
        SubtaskCount++;
        if (open)
            OpenSubtaskCount++;
    }

    /// <summary>
    /// Fija los contadores contra la verdad de las filas. Separado de los <c>Register*</c> porque un
    /// ajuste no es un hecho de negocio y no emite eventos.
    /// </summary>
    /// <summary>
    /// Lo llama el barrido de vencidos tras publicar el aviso. Público como los <c>MarkAttachment*</c>
    /// y por lo mismo: lo dispara un proceso de fondo, no una acción del usuario. Idempotente.
    /// </summary>
    public bool MarkOverdueNotified(DateTime nowUtc)
    {
        if (OverdueNotifiedAtUtc is not null)
            return false;

        OverdueNotifiedAtUtc = nowUtc;
        return true;
    }

    internal void ReconcileCounters(int openBlockerCount, int openSubtaskCount, int subtaskCount)
    {
        OpenBlockerCount = openBlockerCount < 0 ? 0 : openBlockerCount;
        OpenSubtaskCount = openSubtaskCount < 0 ? 0 : openSubtaskCount;
        SubtaskCount = subtaskCount < 0 ? 0 : subtaskCount;
    }
}
