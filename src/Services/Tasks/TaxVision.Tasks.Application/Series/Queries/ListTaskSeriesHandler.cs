using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Domain.Series;

namespace TaxVision.Tasks.Application.Series.Queries;

public sealed record ListTaskSeriesQuery(Guid TenantId, SeriesStatus? Status);

public static class ListTaskSeriesHandler
{
    public static async Task<IReadOnlyList<TaskSeriesResponse>> Handle(
        ListTaskSeriesQuery query,
        ITaskSeriesRepository seriesRepository,
        CancellationToken ct
    )
    {
        var series = await seriesRepository.ListAsync(query.TenantId, query.Status, ct);
        return [.. series.Select(TaskSeriesResponse.From)];
    }
}
