using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Tasks.Queries;
using TaxVision.Tasks.Domain.Series;

namespace TaxVision.Tasks.Application.Series.Queries;

/// <param name="Mine">Solo las que creé o me tocan.</param>
public sealed record ListTaskSeriesQuery(
    Guid TenantId,
    SeriesStatus? Status,
    Guid ActorUserId,
    bool CanViewAll,
    bool Mine,
    int Page,
    int Size
);

public sealed record TaskSeriesPage(IReadOnlyList<TaskSeriesResponse> Items, int TotalCount);

/// <summary>Las series con la misma regla que las tareas: sin ver todos los clientes, las del cliente ajeno no salen.</summary>
public static class ListTaskSeriesHandler
{
    public const int MaxSize = 100;

    public static async Task<TaskSeriesPage> Handle(
        ListTaskSeriesQuery query,
        ITaskSeriesRepository seriesRepository,
        IOptions<TasksVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var filter = new SeriesListFilter(
            query.TenantId,
            query.Status,
            query.Mine ? query.ActorUserId : null,
            TaskReadGate.ViewerOf(visibility, query.ActorUserId, query.CanViewAll),
            Math.Max(1, query.Page),
            Math.Clamp(query.Size, 1, MaxSize)
        );
        var (items, total) = await seriesRepository.ListPageAsync(filter, ct);
        return new TaskSeriesPage([.. items.Select(TaskSeriesResponse.From)], total);
    }
}
