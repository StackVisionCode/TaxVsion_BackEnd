using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Common.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Application.Tasks.Commands;

/// <summary>Qué cambió al guardar el formulario.</summary>
public readonly record struct TaskEditOutcome(bool Any, bool DueChanged, bool ReferenceChanged);

/// <summary>El formulario validado entero antes de tocar la tarea: un campo inválido no la deja a medias.</summary>
internal sealed record TaskEditForm(
    TaskTitle Title,
    TaskDescription? Description,
    TaskPriority Priority,
    DueDate? Due,
    string? StatutoryChangeReason,
    Guid? AssigneeUserId,
    EstimatedHours? Estimated,
    TaskReference Reference
)
{
    public static async Task<Result<TaskEditForm>> ParseAsync(
        UpdateTaskCommand command,
        IOfficeTimeZone zones,
        CancellationToken ct
    )
    {
        var draft = TaskDraft.From(command.Title, command.Description, null, null, false, null, null, null);
        if (draft.IsFailure)
            return Result.Failure<TaskEditForm>(draft.Error);

        var zone = command.DueOn is null ? null : await zones.ResolveAsync(command.TenantId, ct);
        var due = TaskDraft.BuildDue(null, null, command.IsStatutory, command.DueOn, zone);
        if (due is { IsFailure: true })
            return Result.Failure<TaskEditForm>(due.Error);

        var estimate = EstimateOf(command.EstimatedMinutes);
        if (estimate.IsFailure)
            return Result.Failure<TaskEditForm>(estimate.Error);

        var reference = TaskReference.Create(command.CustomerId, command.TaxYear);
        if (reference.IsFailure)
            return Result.Failure<TaskEditForm>(reference.Error);

        return Result.Success(
            new TaskEditForm(
                draft.Value.Title,
                draft.Value.Description,
                command.Priority,
                due?.Value,
                command.StatutoryChangeReason,
                command.AssigneeUserId,
                estimate.Value,
                reference.Value
            )
        );
    }

    /// <summary>Aplica solo lo distinto; si un paso falla, el handler no guarda y la transacción no existe.</summary>
    public Result<TaskEditOutcome> ApplyTo(TaskItem task, Guid byUserId, DateTime nowUtc)
    {
        // Por fecha civil: lo viejo guardado a medianoche UTC y el fin del día en la oficina son el mismo día.
        var dueChanged = (Due?.DueOn, Due?.IsStatutory) != (task.Due?.DueOn, task.Due?.IsStatutory);
        var referenceChanged = Reference != task.Reference;
        var steps = new List<Func<Result>>();
        if (referenceChanged)
            steps.Add(() => task.ChangeReference(Reference, byUserId, nowUtc));
        if (Title != task.Title)
            steps.Add(() => task.ChangeTitle(Title));
        if (Description != task.Description)
            steps.Add(() => task.ChangeDescription(Description));
        if (Priority != task.Priority)
            steps.Add(() => task.ChangePriority(Priority, byUserId, nowUtc));
        if (dueChanged)
            steps.Add(() => task.ChangeDue(Due, byUserId, nowUtc, StatutoryChangeReason));
        if (AssigneeUserId != task.AssigneeUserId)
            steps.Add(() =>
                AssigneeUserId is { } to ? task.Assign(to, byUserId, nowUtc) : task.Unassign(byUserId, nowUtc)
            );
        if (Estimated != task.Estimated)
            steps.Add(() => ChangeEstimate(task));

        foreach (var step in steps)
        {
            var applied = step();
            if (applied.IsFailure)
                return Result.Failure<TaskEditOutcome>(applied.Error);
        }
        return Result.Success(new TaskEditOutcome(steps.Count > 0, dueChanged, referenceChanged));
    }

    private Result ChangeEstimate(TaskItem task)
    {
        task.ChangeEstimate(Estimated);
        return Result.Success();
    }

    private static Result<EstimatedHours?> EstimateOf(int? minutes)
    {
        if (minutes is null)
            return Result.Success<EstimatedHours?>(null);
        var created = EstimatedHours.Create(Math.Round(minutes.Value / 60m, EstimatedHours.Scale));
        return created.IsFailure
            ? Result.Failure<EstimatedHours?>(created.Error)
            : Result.Success<EstimatedHours?>(created.Value);
    }
}
