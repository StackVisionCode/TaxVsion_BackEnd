using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Infrastructure.Persistence.Repositories;

public sealed class TaskSeriesRepository(TasksDbContext context) : ITaskSeriesRepository
{
    public void Add(TaskSeries series) => context.TaskSeries.Add(series);

    public async Task<Result<TaskSeries>> GetByIdAsync(Guid tenantId, Guid seriesId, CancellationToken ct = default)
    {
        var series = await context
            .TaskSeries.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == seriesId, ct);

        return series is null ? Result.Failure<TaskSeries>(TaskErrors.Series.NotFound) : Result.Success(series);
    }

    public async Task<IReadOnlyList<TaskSeries>> ListAsync(
        Guid tenantId,
        SeriesStatus? status,
        CancellationToken ct = default
    )
    {
        var query = context.TaskSeries.IgnoreQueryFilters().Where(s => s.TenantId == tenantId);
        if (status is { } wanted)
            query = query.Where(s => s.Status == wanted);

        return await query.OrderByDescending(s => s.CreatedAtUtc).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TaskSeries>> ListPendingMaterializationAsync(
        int take,
        CancellationToken ct = default
    ) =>
        await context
            .TaskSeries.IgnoreQueryFilters()
            .Where(s => s.Status == SeriesStatus.Active && s.OpenInstanceId == null)
            .OrderBy(s => s.CreatedAtUtc)
            .Take(take)
            .ToListAsync(ct);
}
