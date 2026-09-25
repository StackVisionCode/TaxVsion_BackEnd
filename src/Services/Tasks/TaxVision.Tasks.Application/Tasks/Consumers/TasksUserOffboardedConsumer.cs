using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using Wolverine;

namespace TaxVision.Tasks.Application.Tasks.Consumers;

/// <summary>
/// Al RETIRAR (offboard) a un empleado: (1) sus tareas ABIERTAS asignadas se reasignan al sucesor (o se
/// DESASIGNAN si no hay sucesor, para que la oficina las retome) y (2) las series cuyo asignado por
/// defecto es él redirigen sus ocurrencias futuras al sucesor (o se PAUSAN si no hay sucesor). NO se
/// tocan: tareas terminadas (Completed/Cancelled), `CreatedByUserId`/timers/adjuntos (procedencia), ni
/// las `ClientRequest` (peticiones de cliente, aggregate aparte sin asignado). Idempotente.
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
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<TaskItem> logger,
        CancellationToken ct
    )
    {
        using var _ = correlation.Push(CorrelationIdOf(msg));

        var now = DateTime.UtcNow;
        var actor = msg.OffboardedByUserId ?? msg.UserId;
        var successor = msg.SuccessorUserId is { } candidate && candidate != msg.UserId ? candidate : (Guid?)null;

        var reassignedTasks = await ReassignOpenTasksAsync(
            msg,
            tasks,
            bus,
            unitOfWork,
            correlation,
            successor,
            actor,
            now,
            ct
        );
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
        IMessageBus bus,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
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
                changed += await ReassignOneAsync(msg, task, bus, correlation, successor, actor, now) ? 1 : 0;

            await unitOfWork.SaveChangesAsync(ct);
            total += changed;
            if (changed == 0)
                break;
        }

        return total;
    }

    private static async Task<bool> ReassignOneAsync(
        UserOffboardedIntegrationEvent msg,
        TaskItem task,
        IMessageBus bus,
        ICorrelationContext correlation,
        Guid? successor,
        Guid actor,
        DateTime now
    )
    {
        if (successor is not { } newAssignee)
            return task.Unassign(actor, now).IsSuccess;

        var previous = task.AssigneeUserId;
        if (task.Assign(newAssignee, actor, now).IsFailure)
            return false;

        await bus.PublishAsync(
            new TaskAssignedIntegrationEvent
            {
                TenantId = msg.TenantId,
                CorrelationId = correlation.CorrelationId,
                TaskId = task.Id,
                Title = task.Title.Value,
                AssigneeUserId = newAssignee,
                PreviousAssigneeUserId = previous,
                DueAtUtc = task.Due?.DueAtUtc,
            }
        );
        return true;
    }

    /// <summary>
    /// Series cuyo asignado por defecto es el que se va (pocas por tenant → se filtran en memoria sobre el
    /// listado del tenant, sin índice dedicado). Con sucesor redirigen las ocurrencias futuras; sin él se pausan.
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
            .Where(s => s.Blueprint.AssigneeUserId == msg.UserId && s.Status != SeriesStatus.Ended)
            .ToList();

        foreach (var affectedSeries in affected)
        {
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
