using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Series.Abstractions;

namespace TaxVision.Tasks.Application.Series.Queries;

public sealed record GetTaskSeriesByIdQuery(Guid TenantId, Guid SeriesId);

public static class GetTaskSeriesByIdHandler
{
    public static async Task<Result<TaskSeriesResponse>> Handle(
        GetTaskSeriesByIdQuery query,
        ITaskSeriesRepository seriesRepository,
        CancellationToken ct
    )
    {
        var found = await seriesRepository.GetByIdAsync(query.TenantId, query.SeriesId, ct);
        return found.IsFailure
            ? Result.Failure<TaskSeriesResponse>(found.Error)
            : Result.Success(TaskSeriesResponse.From(found.Value));
    }
}
