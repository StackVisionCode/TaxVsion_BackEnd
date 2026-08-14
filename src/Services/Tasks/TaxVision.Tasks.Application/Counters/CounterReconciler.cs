using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.Application.Counters.Abstractions;
using TaxVision.Tasks.Application.Dependencies.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Counters;

public sealed class CounterReconciler(
    ITaskRepository tasks,
    ITaskDependencyRepository dependencies,
    IUnitOfWork unitOfWork,
    ILogger<CounterReconciler> logger
) : ICounterReconciler
{
    public async Task<int> ReconcileAsync(int take, CancellationToken ct = default)
    {
        var mismatches = await dependencies.ListCounterMismatchesAsync(take, ct);
        if (mismatches.Count == 0)
            return 0;

        var fixedCount = 0;
        foreach (var tenantGroup in mismatches.GroupBy(m => m.TenantId))
        {
            var byTaskId = tenantGroup.ToDictionary(m => m.TaskId);
            var affected = await tasks.ListByIdsAsync(tenantGroup.Key, byTaskId.Keys, ct);

            foreach (var task in affected)
            {
                var mismatch = byTaskId[task.Id];

                // WARN: si un contador se desvió, algo lo escribió por fuera del dominio.
                logger.LogWarning(
                    "Counters out of sync for task {TaskId} (tenant {TenantId}): blockers {StoredBlockers}→{ActualBlockers}, subtasks {StoredSubtasks}→{ActualSubtasks}.",
                    task.Id,
                    mismatch.TenantId,
                    mismatch.StoredBlockers,
                    mismatch.ActualBlockers,
                    mismatch.StoredSubtasks,
                    mismatch.ActualSubtasks
                );

                task.ReconcileCounters(mismatch.ActualBlockers, mismatch.ActualSubtasks);
                fixedCount++;
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
        return fixedCount;
    }
}
