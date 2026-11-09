using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.Application.Reminders;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using Wolverine;

namespace TaxVision.Tasks.Application.Tasks.Consumers;

/// <summary>
/// Al RETIRAR (offboard) a un empleado: (1) sus tareas ABIERTAS asignadas se reasignan al sucesor (o se
/// DESASIGNAN si no hay sucesor, para que la oficina las retome) y (2) las series cuyo asignado por
/// defecto es él redirigen sus ocurrencias futuras al sucesor (o se PAUSAN si no hay sucesor); las
/// que él gestionaba pasan al sucesor (o a quien ejecutó el retiro). El sucesor pide su recordatorio de vencimiento. NO se tocan: tareas terminadas, `CreatedByUserId`/adjuntos
/// (procedencia). Timers y `ClientRequest` tienen sus propios consumers del retiro. Idempotente.
/// </summary>
public static class TasksUserOffboardedConsumer
{
    private const int BatchSize = 200;

    private static string CorrelationIdOf(UserOffboardedIntegrationEvent msg) =>
        string.IsNullOrWhiteSpace(msg.CorrelationId) ? msg.EventId.ToString("N") : msg.CorrelationId;

    public static async Task Handle(
        UserOffboardedIntegrationEvent msg,
        ITaskRepository tasks,
        ITaskSeriesRepository series,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TaskItem> logger,
        CancellationToken ct
    )
    {
        using var _ = correlation.Push(CorrelationIdOf(msg));

        var now = DateTime.UtcNow;
        var actor = msg.OffboardedByUserId ?? msg.UserId;
        var successor = msg.SuccessorUserId is { } candidate && candidate != msg.UserId ? candidate : (Guid?)null;

        var reassignedTasks = await ReassignOpenTasksAsync(msg, tasks, unitOfWork, successor, actor, now, ct);
        var affectedSeries = await RedirectSeriesAsync(msg, series, unitOfWork, successor, ct);

        if (reassignedTasks > 0 || affectedSeries > 0)
            logger.LogInformation(
                "Offboarded user {UserId}: {Tasks} open task(s) {TaskAction}, {Series} series {SeriesAction} in tenant {TenantId}.",
                msg.UserId,
                reassignedTasks,
                successor is null ? "unassigned" : "reassigned",
                affectedSeries,
                successor is null ? "paused" : "redirected",
                msg.TenantId
            );
    }

    /// <summary>
    /// Tareas ABIERTAS asignadas al que se va. Se pide siempre la "página 1": cada tarea procesada cambia
    /// de asignado y sale del filtro, así que la siguiente vuelta trae el próximo lote. Corta si un lote
    /// no logra cambiar ninguna (evita ciclar).
    /// </summary>
    private static async Task<int> ReassignOpenTasksAsync(
        UserOffboardedIntegrationEvent msg,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        Guid? successor,
        Guid actor,
        DateTime now,
        CancellationToken ct
    )
    {
        var total = 0;
        while (true)
        {
            var batch = await tasks.ListForAssigneeAsync(msg.TenantId, msg.UserId, null, page: 1, size: BatchSize, ct);
            if (batch.Items.Count == 0)
                break;

            var changed = 0;
            foreach (var task in batch.Items)
                changed += ReassignOne(task, successor, actor, now) ? 1 : 0;

            await unitOfWork.SaveChangesAsync(ct);
            total += changed;
            if (changed == 0)
                break;
        }

        return total;
    }

    // El aviso al sucesor y su recordatorio los devuelve TaskAssignedDomainEventHandler; el del retirado lo
    // cancela Reminder.
    private static bool ReassignOne(TaskItem task, Guid? successor, Guid actor, DateTime now) =>
        successor is { } newAssignee
            ? task.Assign(newAssignee, actor, now).IsSuccess
            : task.Unassign(actor, now).IsSuccess;

    /// <summary>
    /// Series del que se va, como asignado por defecto o como dueño (pocas por tenant → se filtran en memoria
    /// sobre el listado del tenant, sin índice dedicado). Asignado: con sucesor redirigen las ocurrencias
    /// futuras; sin él se pausan. Dueño: la gestión pasa al sucesor o, sin él, a quien ejecutó el retiro, para
    /// que alguien activo pueda editarla y las ocurrencias nuevas no nazcan a nombre del retirado.
    /// </summary>
    private static async Task<int> RedirectSeriesAsync(
        UserOffboardedIntegrationEvent msg,
        ITaskSeriesRepository series,
        IUnitOfWork unitOfWork,
        Guid? successor,
        CancellationToken ct
    )
    {
        var affected = (await series.ListAsync(msg.TenantId, null, ct))
            .Where(s =>
                s.Status != SeriesStatus.Ended
                && (s.Blueprint.AssigneeUserId == msg.UserId || s.EffectiveOwnerUserId == msg.UserId)
            )
            .ToList();

        var newOwner = successor ?? (msg.OffboardedByUserId is { } admin && admin != msg.UserId ? admin : null);
        foreach (var affectedSeries in affected)
        {
            if (affectedSeries.EffectiveOwnerUserId == msg.UserId && newOwner is { } owner)
                affectedSeries.TransferOwnership(owner);

            if (affectedSeries.Blueprint.AssigneeUserId != msg.UserId)
                continue;
            if (successor is { } newAssignee)
                affectedSeries.ReassignBlueprintAssignee(newAssignee);
            else
                affectedSeries.Pause();
        }

        if (affected.Count > 0)
            await unitOfWork.SaveChangesAsync(ct);

        return affected.Count;
    }
}
