using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Common.Abstractions;
using TaxVision.Tasks.Application.Statuses.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Tasks.Queries;

/// <param name="Skip">El cursor de "Load more": cuántas tarjetas de la columna ya se tienen.</param>
public sealed record GetTaskBoardColumnQuery(
    Guid TenantId,
    TaskQueryFilter Filter,
    string Code,
    int Skip,
    int Take,
    TaskBoardSort Sort,
    Guid ActorUserId,
    bool CanViewAll,
    TaskDueBucket? Due = null,
    DateTime NowUtc = default
);

/// <summary>"Load more" de una columna del tablero.</summary>
public static class GetTaskBoardColumnHandler
{
    public static async Task<Result<TaskBoardColumnSlice>> Handle(
        GetTaskBoardColumnQuery query,
        ITaskBoardReader reader,
        IWorkflowStatusRepository statuses,
        IOfficeTimeZone zones,
        IOptions<TasksVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var columns = await TaskBoardColumns.ListAsync(statuses, query.TenantId, ct);
        var column = columns.FirstOrDefault(c => c.Code == query.Code.Trim().ToLowerInvariant());
        if (column is null)
            return Result.Failure<TaskBoardColumnSlice>(TaskErrors.Status.NotFound);

        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        var filter = await TaskDueFilters.ApplyAsync(query.TenantId, query.Filter, query.Due, query.NowUtc, zones, ct);
        var tallies = await reader.CountAsync(query.TenantId, filter, assignedTo, ct);
        var page = (Math.Max(0, query.Skip), TaskBoardColumns.Take(query.Take), query.Sort);
        var total = TaskBoardColumns.TotalOf(column, columns, tallies);
        return Result.Success(
            await TaskBoardColumns.SliceAsync(
                reader,
                query.TenantId,
                filter,
                column,
                columns,
                total,
                page,
                assignedTo,
                ct
            )
        );
    }
}
