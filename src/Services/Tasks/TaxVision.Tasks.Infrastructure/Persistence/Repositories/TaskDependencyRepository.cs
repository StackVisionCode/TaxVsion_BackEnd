using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Dependencies.Abstractions;
using TaxVision.Tasks.Domain.Dependencies;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Infrastructure.Persistence.ReadModels;

namespace TaxVision.Tasks.Infrastructure.Persistence.Repositories;

public sealed class TaskDependencyRepository(TasksDbContext context) : ITaskDependencyRepository
{
    private static readonly TaskItemStatus[] OpenStatuses = TaskItemStatuses.Open;

    public void Add(TaskDependency dependency) => context.TaskDependencies.Add(dependency);

    public void Remove(TaskDependency dependency) => context.TaskDependencies.Remove(dependency);

    public Task<TaskDependency?> GetAsync(
        Guid tenantId,
        Guid taskId,
        Guid dependsOnTaskId,
        CancellationToken ct = default
    ) =>
        context
            .TaskDependencies.IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
            .FirstOrDefaultAsync(
                d => d.TenantId == tenantId && d.TaskId == taskId && d.DependsOnTaskId == dependsOnTaskId,
                ct
            );

    public async Task<IReadOnlyList<TaskDependency>> ListTouchingAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> taskIds,
        CancellationToken ct = default
    ) =>
        await context
            .TaskDependencies.IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
            .Where(d => d.TenantId == tenantId && (taskIds.Contains(d.TaskId) || taskIds.Contains(d.DependsOnTaskId)))
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> LoadUpstreamGraphAsync(
        Guid tenantId,
        Guid startTaskId,
        CancellationToken ct = default
    )
    {
        // CTE recursivo: no hay forma en LINQ y la alternativa es un N+1 por nivel. Va por
        // FromSqlRaw, no por ADO crudo, para entrar en la transacción del handler. Sin componer
        // nada encima: el OPTION tiene que quedar al final del statement.
        var sql = $$"""
            WITH Graph AS (
                SELECT TaskId, DependsOnTaskId, 0 AS Lvl
                FROM   TaskDependencies WHERE TenantId = @tenantId AND TaskId = @startTaskId
                UNION ALL
                SELECT d.TaskId, d.DependsOnTaskId, g.Lvl + 1
                FROM   TaskDependencies d
                JOIN   Graph g ON d.TaskId = g.DependsOnTaskId
                WHERE  d.TenantId = @tenantId AND g.Lvl < {{TaskDependencyGraph.MaxQueryLevel}}
            )
            SELECT TOP ({{TaskDependencyGraph.MaxQueryEdges}}) TaskId, DependsOnTaskId FROM Graph OPTION (MAXRECURSION 100)
            """;

        var rows = await context
            .Set<TaskDependencyEdge>()
            .FromSqlRaw(sql, new SqlParameter("@tenantId", tenantId), new SqlParameter("@startTaskId", startTaskId))
            .ToListAsync(ct);

        var edges = new Dictionary<Guid, List<Guid>>();
        foreach (var row in rows)
        {
            if (!edges.TryGetValue(row.TaskId, out var predecessors))
                edges[row.TaskId] = predecessors = [];
            predecessors.Add(row.DependsOnTaskId);
        }

        return edges.ToDictionary(e => e.Key, e => (IReadOnlyList<Guid>)e.Value);
    }

    public Task LockTenantEdgesAsync(Guid tenantId, CancellationToken ct = default) =>
        context.Database.ExecuteSqlRawAsync(
            "SELECT TOP (1) Id FROM TaskDependencies WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = @tenantId",
            [new SqlParameter("@tenantId", tenantId)],
            ct
        );

    public Task<int> CountOpenBlockersAsync(Guid tenantId, Guid taskId, CancellationToken ct = default) =>
        context
            .TaskDependencies.IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
            .Where(d => d.TenantId == tenantId && d.TaskId == taskId)
            .Join(
                context.Tasks.IgnoreQueryFilters(TaskQueryFilters.TenantOnly).Where(t => t.TenantId == tenantId),
                d => d.DependsOnTaskId,
                t => t.Id,
                (d, t) => t.Status
            )
            .CountAsync(status => OpenStatuses.Contains(status), ct);

    public async Task<IReadOnlyList<Guid>> ListSuccessorIdsAsync(
        Guid tenantId,
        Guid dependsOnTaskId,
        CancellationToken ct = default
    ) =>
        await context
            .TaskDependencies.IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
            .Where(d => d.TenantId == tenantId && d.DependsOnTaskId == dependsOnTaskId)
            .Select(d => d.TaskId)
            .ToListAsync(ct);

    /// <summary>Arranca de <c>Tasks</c>: una tarea con contador y sin filas reales también descuadra.</summary>
    public async Task<IReadOnlyList<CounterMismatch>> ListCounterMismatchesAsync(
        int take,
        CancellationToken ct = default
    ) =>
        await context
            .Tasks.IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
            .Select(t => new
            {
                t.TenantId,
                TaskId = t.Id,
                StoredBlockers = t.OpenBlockerCount,
                ActualBlockers = context
                    .TaskDependencies.IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
                    .Count(d =>
                        d.TenantId == t.TenantId
                        && d.TaskId == t.Id
                        && context
                            .Tasks.IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
                            .Any(p =>
                                p.Id == d.DependsOnTaskId && p.TenantId == d.TenantId && OpenStatuses.Contains(p.Status)
                            )
                    ),
                StoredSubtasks = t.OpenSubtaskCount,
                ActualSubtasks = context
                    .Tasks.IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
                    .Count(c => c.TenantId == t.TenantId && c.ParentTaskId == t.Id && OpenStatuses.Contains(c.Status)),
                StoredSubtaskTotal = t.SubtaskCount,
                ActualSubtaskTotal = context
                    .Tasks.IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
                    .Count(c => c.TenantId == t.TenantId && c.ParentTaskId == t.Id),
            })
            .Where(x =>
                x.StoredBlockers != x.ActualBlockers
                || x.StoredSubtasks != x.ActualSubtasks
                || x.StoredSubtaskTotal != x.ActualSubtaskTotal
            )
            .Take(take)
            .Select(x => new CounterMismatch(
                x.TenantId,
                x.TaskId,
                x.StoredBlockers,
                x.ActualBlockers,
                x.StoredSubtasks,
                x.ActualSubtasks,
                x.StoredSubtaskTotal,
                x.ActualSubtaskTotal
            ))
            .ToListAsync(ct);
}
