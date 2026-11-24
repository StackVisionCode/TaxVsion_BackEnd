using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.Persistence.Repositories;

public sealed class TaskSeriesRepository(TasksDbContext context) : ITaskSeriesRepository
{
    public void Add(TaskSeries series) => context.TaskSeries.Add(series);

    public async Task<Result<TaskSeries>> GetByIdAsync(Guid tenantId, Guid seriesId, CancellationToken ct = default)
    {
        var series = await context
            .TaskSeries.IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == seriesId, ct);

        return series is null ? Result.Failure<TaskSeries>(TaskErrors.Series.NotFound) : Result.Success(series);
    }

    public void ExpectVersion(TaskSeries series, byte[] rowVersion) =>
        context.Entry(series).Property(s => s.RowVersion).OriginalValue = rowVersion;

    public async Task<IReadOnlyList<TaskSeries>> ListAsync(
        Guid tenantId,
        SeriesStatus? status,
        CancellationToken ct = default
    )
    {
        var query = context
            .TaskSeries.IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
            .Where(s => s.TenantId == tenantId);
        if (status is { } wanted)
            query = query.Where(s => s.Status == wanted);

        return await query.OrderByDescending(s => s.CreatedAtUtc).ToListAsync(ct);
    }

    public async Task<(IReadOnlyList<TaskSeries> Items, int Total)> ListPageAsync(
        SeriesListFilter filter,
        CancellationToken ct = default
    )
    {
        var query = context
            .TaskSeries.IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
            .Where(s => s.TenantId == filter.TenantId);
        if (filter.Status is { } wanted)
            query = query.Where(s => s.Status == wanted);
        if (filter.OnlyFor is { } mine)
            query = query.Where(s =>
                (s.OwnerUserId ?? s.CreatedByUserId) == mine || s.Blueprint.AssigneeUserId == mine
            );
        if (filter.VisibleTo is { } viewer)
            query = query.Where(s =>
                (s.OwnerUserId ?? s.CreatedByUserId) == viewer
                || s.Blueprint.AssigneeUserId == viewer
                || s.Blueprint.Reference.CustomerId == null
                || context
                    .Set<CustomerAssignmentProjection>()
                    .IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
                    .Any(a =>
                        a.TenantId == filter.TenantId
                        && a.UserId == viewer
                        && a.CustomerId == s.Blueprint.Reference.CustomerId
                    )
            );

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(s => s.CreatedAtUtc)
            .Skip((filter.Page - 1) * filter.Size)
            .Take(filter.Size)
            .ToListAsync(ct);
        return (items, total);
    }

    // Cross-tenant: el job materializa las series de todas las oficinas.
    public async Task<IReadOnlyList<TaskSeries>> ListPendingMaterializationAsync(
        int take,
        DateTime nowUtc,
        CancellationToken ct = default
    ) =>
        await context
            .TaskSeries.IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
            .Where(s =>
                s.Status == SeriesStatus.Active
                && s.OpenInstanceId == null
                && (s.NextMaterializationAtUtc == null || s.NextMaterializationAtUtc <= nowUtc)
            )
            .OrderBy(s => s.CreatedAtUtc)
            .Take(take)
            .ToListAsync(ct);
}
