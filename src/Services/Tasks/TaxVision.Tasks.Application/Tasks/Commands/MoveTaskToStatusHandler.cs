using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using TaxVision.Tasks.Application.Common.Abstractions;
using TaxVision.Tasks.Application.Dependencies.Abstractions;
using TaxVision.Tasks.Application.Hierarchy.Abstractions;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Application.Statuses;
using TaxVision.Tasks.Application.Statuses.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Statuses;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Tasks.Application.Tasks.Commands;

/// <param name="Status">Código del estado destino (de la oficina o del sistema).</param>
/// <param name="ExpectedItems">Lo que se pide al cliente, si el destino es "Waiting on client".</param>
/// <param name="ClientDueOn">Para cuándo, en el día de la oficina.</param>
/// <param name="CancelReason">Motivo, si el destino es "Cancelled".</param>
public sealed record MoveTaskToStatusCommand(
    Guid TenantId,
    Guid TaskId,
    Guid ByUserId,
    bool HasManageAll,
    string Status,
    string? ExpectedItems = null,
    DateOnly? ClientDueOn = null,
    string? CancelReason = null,
    bool CompleteOpenSubtasks = false
) : ITaskTransitionCommand;

/// <summary>
/// El movimiento único: una llamada, una transacción. Valida todo antes de tocar la tarea, porque Wolverine
/// guarda al final aunque la orden falle; las cascadas son las de los verbos (<see cref="TaskTransitionService"/>).
/// </summary>
public static class MoveTaskToStatusHandler
{
    public static async Task<Result<TaskResponse>> Handle(
        MoveTaskToStatusCommand command,
        ITaskRepository tasks,
        IWorkflowStatusRepository statuses,
        ITaskUnblockingService unblocking,
        ITaskHierarchyService hierarchy,
        ITaskSeriesMaterializer seriesMaterializer,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        IOfficeTimeZone zones,
        CancellationToken ct
    )
    {
        var prepared = await PrepareAsync(command, tasks, statuses, zones, seriesMaterializer, ct);
        if (prepared.IsFailure)
            return Result.Failure<TaskResponse>(prepared.Error);

        var (task, target, request) = prepared.Value;
        var moved = await MoveAsync(
            command,
            task,
            target,
            request,
            tasks,
            unblocking,
            hierarchy,
            seriesMaterializer,
            ct
        );
        if (moved.IsFailure)
            return Result.Failure<TaskResponse>(moved.Error);

        await unitOfWork.SaveChangesAsync(ct);
        foreach (var notice in moved.Value.Notices)
            await bus.PublishAsync(notice);
        if (moved.Value.Outcome.MovedToWaitingOnClient)
            await AnnounceWaitingOnClientAsync(task, zones, bus, correlation, ct);

        return Result.Success(TaskResponse.From(task));
    }

    private sealed record Moved(TaskMoveOutcome Outcome, IReadOnlyList<TaskUnblockedIntegrationEvent> Notices);

    /// <summary>La tarea con permiso, el estado destino validado y lo que pide la etapa destino.</summary>
    private static async Task<Result<(TaskItem, WorkflowStatusRef, TaskMoveRequest)>> PrepareAsync(
        MoveTaskToStatusCommand command,
        ITaskRepository tasks,
        IWorkflowStatusRepository statuses,
        IOfficeTimeZone zones,
        ITaskSeriesMaterializer seriesMaterializer,
        CancellationToken ct
    )
    {
        var found = await TaskTransitionService.LoadMutableAsync(tasks, command, ct);
        if (found.IsFailure)
            return Result.Failure<(TaskItem, WorkflowStatusRef, TaskMoveRequest)>(found.Error);

        var target = await WorkflowStatusCatalog.ResolveAsync(
            statuses,
            command.TenantId,
            command.Status,
            command.ByUserId,
            DateTime.UtcNow,
            ct
        );
        if (target.IsFailure)
            return Result.Failure<(TaskItem, WorkflowStatusRef, TaskMoveRequest)>(target.Error);
        var reopens = IsClosed(found.Value.Status) && !IsClosed(target.Value.Category);
        var allowed = reopens ? await seriesMaterializer.CanReopenAsync(found.Value, ct) : Result.Success();
        if (allowed.IsFailure)
            return Result.Failure<(TaskItem, WorkflowStatusRef, TaskMoveRequest)>(allowed.Error);

        var request = await RequestForAsync(command, found.Value, target.Value, tasks, zones, ct);
        return request.IsFailure
            ? Result.Failure<(TaskItem, WorkflowStatusRef, TaskMoveRequest)>(request.Error)
            : Result.Success((found.Value, target.Value, request.Value));
    }

    private static async Task<Result<TaskMoveRequest>> RequestForAsync(
        MoveTaskToStatusCommand command,
        TaskItem task,
        WorkflowStatusRef target,
        ITaskRepository tasks,
        IOfficeTimeZone zones,
        CancellationToken ct
    )
    {
        if (await ReopensUnderClosedParentAsync(task, target, tasks, ct))
            return Result.Failure<TaskMoveRequest>(TaskErrors.Subtask.ParentClosed);
        if (target.Category != TaskItemStatus.WaitingOnClient || target.Category == task.Status)
            return Result.Success(new TaskMoveRequest(null, null, null, command.CancelReason));
        if (task.Reference.CustomerId is null)
            return Result.Failure<TaskMoveRequest>(TaskErrors.WaitingOnClient.CustomerRequired);

        var note = ClientRequestNote.Create(command.ExpectedItems);
        if (note.IsFailure)
            return Result.Failure<TaskMoveRequest>(note.Error);

        DateTime? dueUtc = command.ClientDueOn is { } civil
            ? LocalDay.EndUtc(civil, await zones.ResolveAsync(command.TenantId, ct))
            : null;
        return Result.Success(new TaskMoveRequest(note.Value, dueUtc, command.ClientDueOn, command.CancelReason));
    }

    /// <summary>D-7: una subtarea no se reabre bajo una tarea cerrada; eso se hace con Reopen y su permiso.</summary>
    private static async Task<bool> ReopensUnderClosedParentAsync(
        TaskItem task,
        WorkflowStatusRef target,
        ITaskRepository tasks,
        CancellationToken ct
    )
    {
        var reopens = IsClosed(task.Status) && !IsClosed(target.Category);
        if (!reopens || task.ParentTaskId is null)
            return false;

        var ancestors = await tasks.ListByIdsAsync(
            task.TenantId,
            await tasks.GetAncestorIdsAsync(task.TenantId, task.Id, ct),
            ct
        );
        return ancestors.Any(a => IsClosed(a.Status));
    }

    private static async Task<Result<Moved>> MoveAsync(
        MoveTaskToStatusCommand command,
        TaskItem task,
        WorkflowStatusRef target,
        TaskMoveRequest request,
        ITaskRepository tasks,
        ITaskUnblockingService unblocking,
        ITaskHierarchyService hierarchy,
        ITaskSeriesMaterializer seriesMaterializer,
        CancellationToken ct
    )
    {
        var now = DateTime.UtcNow;
        var notices = new List<TaskUnblockedIntegrationEvent>();
        if (command.CompleteOpenSubtasks && target.Category == TaskItemStatus.Completed && !IsClosed(task.Status))
        {
            var subtasks = await TaskTransitionService.CompleteOpenSubtasksAsync(
                tasks,
                task,
                command.ByUserId,
                now,
                unblocking,
                hierarchy,
                seriesMaterializer,
                ct
            );
            if (subtasks.IsFailure)
                return Result.Failure<Moved>(subtasks.Error);
            notices.AddRange(subtasks.Value);
        }

        var outcome = task.MoveTo(target, request, command.ByUserId, now);
        if (outcome.IsFailure)
            return Result.Failure<Moved>(outcome.Error);

        if (outcome.Value.Reopened)
            await TaskTransitionService.ApplyReopenedAsync(task, unblocking, hierarchy, ct, seriesMaterializer);
        if (outcome.Value.Closed)
            notices.AddRange(
                await TaskTransitionService.ApplyClosedAsync(task, unblocking, hierarchy, seriesMaterializer, ct)
            );
        return Result.Success(new Moved(outcome.Value, notices));
    }

    private static async Task AnnounceWaitingOnClientAsync(
        TaskItem task,
        IOfficeTimeZone zones,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var officeTimeZoneId = (await zones.ResolveAsync(task.TenantId, ct)).Id;
        await bus.PublishAsync(
            MoveTaskToWaitingOnClientHandler.BuildEvent(
                task,
                task.Reference.CustomerId!.Value,
                correlation.CorrelationId
            )
        );
        await MoveTaskToWaitingOnClientHandler.RequestPreparerReminderAsync(
            task,
            task.ClientDueAtUtc,
            officeTimeZoneId,
            bus,
            correlation
        );
    }

    private static bool IsClosed(TaskItemStatus status) => TaskItemStatuses.IsClosed(status);
}
