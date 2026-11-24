using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Series.Queries;

public sealed record ListSeriesOccurrencesQuery(Guid TenantId, Guid SeriesId, Guid ActorUserId, bool CanViewAll);

public sealed record SeriesOccurrenceResponse(
    Guid TaskId,
    int? OccurrenceNumber,
    string Title,
    string Status,
    string? WorkflowStatusCode,
    DateOnly? DueOn,
    DateTime? CompletedAtUtc
);

/// <summary>Las tareas que generó la serie (las últimas 50), con la misma visibilidad que la serie.</summary>
public static class ListSeriesOccurrencesHandler
{
    public const int Take = 50;

    public static async Task<Result<IReadOnlyList<SeriesOccurrenceResponse>>> Handle(
        ListSeriesOccurrencesQuery query,
        ITaskSeriesRepository seriesRepository,
        ITaskRepository tasks,
        ITaskCustomerAccess customers,
        IOptions<TasksVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var series = await GetTaskSeriesByIdHandler.Handle(
            new GetTaskSeriesByIdQuery(query.TenantId, query.SeriesId, query.ActorUserId, query.CanViewAll),
            seriesRepository,
            customers,
            visibility,
            ct
        );
        if (series.IsFailure)
            return Result.Failure<IReadOnlyList<SeriesOccurrenceResponse>>(series.Error);

        var occurrences = await tasks.ListBySeriesAsync(query.TenantId, query.SeriesId, Take, ct);
        return Result.Success<IReadOnlyList<SeriesOccurrenceResponse>>([
            .. occurrences.Select(t => new SeriesOccurrenceResponse(
                t.Id,
                t.OccurrenceNumber,
                t.Title.Value,
                t.Status.ToString(),
                t.WorkflowStatusCode,
                t.Due?.DueOn,
                t.CompletedAtUtc
            )),
        ]);
    }
}
