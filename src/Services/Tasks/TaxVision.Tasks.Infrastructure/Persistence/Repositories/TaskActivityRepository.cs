using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Activity.Abstractions;
using TaxVision.Tasks.Domain.Activity;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.Persistence.Repositories;

public sealed class TaskActivityRepository(TasksDbContext context) : ITaskActivityRepository
{
    public void Add(TaskActivity activity) => context.TaskActivities.Add(activity);

    // Tenant explícito: lo escriben handlers de la cola, sin tenant ambiental.
    public async Task<IReadOnlyList<TaskActivity>> ListAsync(
        Guid tenantId,
        Guid taskId,
        DateTime? before,
        int take,
        CancellationToken ct = default
    )
    {
        var query = context
            .TaskActivities.IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.TaskId == taskId);
        if (before is { } cutoff)
            query = query.Where(a => a.OccurredAtUtc < cutoff);
        return await query.OrderByDescending(a => a.OccurredAtUtc).ThenBy(a => a.Id).Take(take).ToListAsync(ct);
    }
}
