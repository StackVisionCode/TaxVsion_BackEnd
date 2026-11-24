using TaxVision.Tasks.Application.Common.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Tasks.Queries;

/// <summary>El vencimiento del dashboard aplicado a un filtro, con el día civil de la oficina.</summary>
public static class TaskDueFilters
{
    public static async Task<TaskQueryFilter> ApplyAsync(
        Guid tenantId,
        TaskQueryFilter filter,
        TaskDueBucket? due,
        DateTime nowUtc,
        IOfficeTimeZone zones,
        CancellationToken ct
    )
    {
        if (due is not { } bucket)
            return filter;
        var window = TaskSummaryWindow.For(nowUtc, await zones.ResolveAsync(tenantId, ct));
        return filter with { Due = new TaskDueFilter(bucket, window) };
    }
}
