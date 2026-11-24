using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Series;

/// <summary>Reabrir tareas que son ocurrencias de una serie: se comprueba antes de reabrir y se registra después.</summary>
public static class SeriesOccurrenceReopening
{
    public static async Task<Result> CheckAsync(
        IEnumerable<TaskItem> tasks,
        ITaskSeriesRepository series,
        CancellationToken ct
    )
    {
        foreach (var task in tasks)
        {
            if (task.SeriesId is not { } seriesId || task.OccurrenceNumber is not { } number)
                continue;
            var found = await series.GetByIdAsync(task.TenantId, seriesId, ct);
            var allowed = found.IsSuccess ? found.Value.CanReopenOccurrence(task.Id, number) : Result.Success();
            if (allowed.IsFailure)
                return allowed;
        }
        return Result.Success();
    }

    public static async Task RegisterAsync(TaskItem task, ITaskSeriesRepository series, CancellationToken ct)
    {
        if (task.SeriesId is not { } seriesId || task.OccurrenceNumber is not { } number)
            return;
        var found = await series.GetByIdAsync(task.TenantId, seriesId, ct);
        if (found.IsSuccess)
            found.Value.RegisterInstanceReopened(task.Id, number);
    }
}
