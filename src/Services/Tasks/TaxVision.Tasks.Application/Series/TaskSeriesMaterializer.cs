using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Application.Series;

public sealed class TaskSeriesMaterializer(ITaskRepository tasks, ITaskSeriesRepository series)
    : ITaskSeriesMaterializer
{
    public Task<Result<TaskItem>> MaterializeNextAsync(
        TaskSeries taskSeries,
        DateTime? lastDueUtc,
        DateTime? completedAtUtc,
        CancellationToken ct = default
    ) => Task.FromResult(Materialize(taskSeries, lastDueUtc, completedAtUtc));

    /// <summary>
    /// Todo lo que hace vive en memoria: la serie y la tarea ya están rastreadas por el contexto y las
    /// guarda el handler. Por eso el método público sólo envuelve el resultado.
    /// </summary>
    private Result<TaskItem> Materialize(TaskSeries taskSeries, DateTime? lastDueUtc, DateTime? completedAtUtc)
    {
        var planned = taskSeries.PlanNextOccurrence(lastDueUtc, completedAtUtc, DateTime.UtcNow);
        if (planned.IsFailure)
        {
            // La regla se agotó: la serie termina en silencio, no es un error del usuario.
            if (planned.Error == TaskErrors.Series.NoFurtherOccurrence)
                taskSeries.End();

            return Result.Failure<TaskItem>(planned.Error);
        }

        var occurrence = planned.Value;
        var blueprint = taskSeries.Blueprint;

        var due = DueDate.Create(occurrence.DueAtUtc, taskSeries.Rule.TimeZoneId, blueprint.IsStatutory);
        if (due.IsFailure)
            return Result.Failure<TaskItem>(due.Error);

        var created = TaskItem.Create(
            taskSeries.TenantId,
            taskSeries.CreatedByUserId,
            blueprint.Title,
            blueprint.Description,
            blueprint.Priority,
            blueprint.Reference,
            due.Value,
            blueprint.Estimated,
            blueprint.AssigneeUserId,
            DateTime.UtcNow
        );
        if (created.IsFailure)
            return created;

        var task = created.Value;
        task.AttachToSeries(taskSeries.Id, occurrence.Number);

        // Los contadores se mueven recién acá: si la tarea no se hubiera podido crear, la serie
        // seguiría intacta y el barrido lo reintentaría.
        var registered = taskSeries.RegisterMaterialized(task.Id, occurrence);
        if (registered.IsFailure)
            return Result.Failure<TaskItem>(registered.Error);

        tasks.Add(task);
        return Result.Success(task);
    }

    public async Task<TaskItem?> ApplyInstanceClosedAsync(
        TaskItem task,
        DateTime? completedAtUtc,
        CancellationToken ct = default
    )
    {
        if (task.SeriesId is not { } seriesId)
            return null;

        var found = await series.GetByIdAsync(task.TenantId, seriesId, ct);
        if (found.IsFailure)
            return null;

        var taskSeries = found.Value;
        if (!taskSeries.RegisterInstanceClosed(task.Id))
            return null;

        var next = await MaterializeNextAsync(taskSeries, task.Due?.DueAtUtc, completedAtUtc, ct);
        return next.IsSuccess ? next.Value : null;
    }
}
