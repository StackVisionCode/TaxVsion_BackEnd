using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Tasks.Queries;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Series.Queries;

public sealed record GetTaskSeriesByIdQuery(Guid TenantId, Guid SeriesId, Guid ActorUserId, bool CanViewAll);

/// <summary>Misma regla que la lista: la serie de un cliente que no se ve no existe para quien la pide.</summary>
public static class GetTaskSeriesByIdHandler
{
    public static async Task<Result<TaskSeriesResponse>> Handle(
        GetTaskSeriesByIdQuery query,
        ITaskSeriesRepository seriesRepository,
        ITaskCustomerAccess customers,
        IOptions<TasksVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var found = await seriesRepository.GetByIdAsync(query.TenantId, query.SeriesId, ct);
        if (found.IsFailure)
            return Result.Failure<TaskSeriesResponse>(found.Error);

        var viewer = TaskReadGate.ViewerOf(visibility, query.ActorUserId, query.CanViewAll);
        if (viewer is { } me && !await IsVisibleAsync(found.Value, me, customers, ct))
            return Result.Failure<TaskSeriesResponse>(TaskErrors.Series.NotFound);
        return Result.Success(TaskSeriesResponse.From(found.Value));
    }

    private static async Task<bool> IsVisibleAsync(
        TaskSeries series,
        Guid me,
        ITaskCustomerAccess customers,
        CancellationToken ct
    ) =>
        series.EffectiveOwnerUserId == me
        || series.Blueprint.AssigneeUserId == me
        || await customers.AllowsAsync(series.TenantId, me, false, series.Blueprint.Reference.CustomerId, ct);
}
