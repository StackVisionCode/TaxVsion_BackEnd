using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Common.Abstractions;
using TaxVision.Tasks.Application.Reminders;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Tasks.Application.Tasks.Commands;

/// <param name="StatutoryChangeReason">
/// Obligatoria sólo para aflojar un vencimiento estatutario — posponerlo, quitarlo o desmarcarlo.
/// </param>
public sealed record ChangeTaskDueCommand(
    Guid TenantId,
    Guid TaskId,
    Guid ByUserId,
    bool HasManageAll,
    DateTime? DueAtUtc,
    string? TimeZoneId,
    bool IsStatutory,
    string? StatutoryChangeReason,
    DateOnly? DueOn = null
);

public static class ChangeTaskDueHandler
{
    public static async Task<Result<TaskResponse>> Handle(
        ChangeTaskDueCommand command,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        IOfficeTimeZone zones,
        CancellationToken ct
    )
    {
        var due = await BuildDueAsync(command, zones, ct);
        if (due is { IsFailure: true })
            return Result.Failure<TaskResponse>(due.Error);

        var found = await tasks.GetByIdAsync(command.TenantId, command.TaskId, ct);
        if (found.IsFailure)
            return Result.Failure<TaskResponse>(found.Error);

        var task = found.Value;
        if (!TaskAccessPolicy.CanMutate(task, command.ByUserId, command.HasManageAll))
            return Result.Failure<TaskResponse>(TaskErrors.Forbidden);

        var previousDueAtUtc = task.Due?.DueAtUtc;
        var changed = task.ChangeDue(due?.Value, command.ByUserId, DateTime.UtcNow, command.StatutoryChangeReason);
        if (changed.IsFailure)
            return Result.Failure<TaskResponse>(changed.Error);

        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(BuildEvent(task, previousDueAtUtc, command.ByUserId, correlation.CorrelationId));

        // Quitar el vencimiento no mueve nada: el contrato de Reminder recalcula sobre un ancla, y sin
        // fecha no hay ancla a la que anclarse.
        if (task.Due is not null)
            await bus.PublishAsync(TaskReminderContracts.Moved(task, correlation.CorrelationId));

        return Result.Success(TaskResponse.From(task));
    }

    // Sin fecha es «quitar el vencimiento»; la zona de la oficina solo hace falta para una fecha civil.
    private static async Task<Result<DueDate>?> BuildDueAsync(
        ChangeTaskDueCommand command,
        IOfficeTimeZone zones,
        CancellationToken ct
    )
    {
        var officeZone = command.DueOn is null ? null : await zones.ResolveAsync(command.TenantId, ct);
        return TaskDraft.BuildDue(command.DueAtUtc, command.TimeZoneId, command.IsStatutory, command.DueOn, officeZone);
    }

    private static TaskDueChangedIntegrationEvent BuildEvent(
        TaskItem task,
        DateTime? previousDueAtUtc,
        Guid changedByUserId,
        string correlationId
    ) =>
        new()
        {
            TenantId = task.TenantId,
            CorrelationId = correlationId,
            TaskId = task.Id,
            PreviousDueAtUtc = previousDueAtUtc,
            NewDueAtUtc = task.Due?.DueAtUtc,
            TimeZoneId = task.Due?.TimeZoneId,
            NewDueOn = task.Due?.DueOn,
            ChangedByUserId = changedByUserId,
        };
}
