using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Common.Abstractions;
using TaxVision.Tasks.Application.Statuses.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Tasks.Queries;

/// <param name="Expanded">Códigos de las columnas que traen tarjetas; null = todas. Las demás solo traen su total.</param>
public sealed record GetTaskBoardColumnsQuery(
    Guid TenantId,
    TaskQueryFilter Filter,
    IReadOnlySet<string>? Expanded,
    int Take,
    TaskBoardSort Sort,
    Guid ActorUserId,
    bool CanViewAll,
    TaskDueBucket? Due = null,
    DateTime NowUtc = default
);

/// <summary>Una columna por estado activo, con su total real; tarjetas solo para las columnas pedidas.</summary>
public static class GetTaskBoardColumnsHandler
{
    public static async Task<TaskBoardColumnsResponse> Handle(
        GetTaskBoardColumnsQuery query,
        ITaskBoardReader reader,
        IWorkflowStatusRepository statuses,
        IOfficeTimeZone zones,
        IOptions<TasksVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        var filter = await TaskDueFilters.ApplyAsync(query.TenantId, query.Filter, query.Due, query.NowUtc, zones, ct);
        var columns = await TaskBoardColumns.ListAsync(statuses, query.TenantId, ct);
        var tallies = await reader.CountAsync(query.TenantId, filter, assignedTo, ct);
        var page = (0, TaskBoardColumns.Take(query.Take), query.Sort);

        var slices = new List<TaskBoardColumnSlice>(columns.Count);
        foreach (var column in columns)
        {
            var total = TaskBoardColumns.TotalOf(column, columns, tallies);
            var wanted = query.Expanded is null || query.Expanded.Contains(column.Code);
            slices.Add(
                wanted && total > 0
                    ? await TaskBoardColumns.SliceAsync(
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
                    : new TaskBoardColumnSlice(column.Code, column.Category, total, wanted ? [] : null, null)
            );
        }
        return new TaskBoardColumnsResponse(slices);
    }
}
