using TaxVision.Tasks.Application.Dependencies.Abstractions;
using TaxVision.Tasks.Application.Hierarchy.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Hierarchy;

public sealed class TaskHierarchyService(ITaskRepository tasks, ITaskDependencyRepository dependencies)
    : ITaskHierarchyService
{
    public Task ApplyChildClosedAsync(Guid tenantId, Guid? parentTaskId, CancellationToken ct = default) =>
        MutateParentAsync(tenantId, parentTaskId, parent => parent.RegisterSubtaskClosed(), ct);

    public Task ApplyChildReopenedAsync(Guid tenantId, Guid? parentTaskId, CancellationToken ct = default) =>
        MutateParentAsync(tenantId, parentTaskId, parent => parent.RegisterSubtaskReopened(), ct);

    public async Task<IReadOnlyList<Guid>> DeleteWithDescendantsAsync(
        Guid tenantId,
        Guid taskId,
        Guid byUserId,
        CancellationToken ct = default
    )
    {
        var rootResult = await tasks.GetByIdAsync(tenantId, taskId, ct);
        if (rootResult.IsFailure)
            return [];

        var nowUtc = DateTime.UtcNow;
        var root = rootResult.Value;

        // El padre pierde un hijo abierto sólo si la raíz del borrado lo era; un hijo, siempre.
        if (TaskItemStatuses.IsOpen(root.Status))
            await ApplyChildClosedAsync(tenantId, root.ParentTaskId, ct);
        await MutateParentAsync(tenantId, root.ParentTaskId, parent => parent.RegisterSubtaskRemoved(), ct);

        var deletedIds = new List<Guid>();
        var level = new List<TaskItem> { root };
        while (level.Count > 0)
        {
            foreach (var task in level)
            {
                task.Delete(byUserId, nowUtc, root.Id);
                deletedIds.Add(task.Id);
            }

            var childIds = await tasks.ListChildIdsAsync(tenantId, [.. level.Select(t => t.Id)], ct);
            level = childIds.Count == 0 ? [] : [.. await tasks.ListByIdsAsync(tenantId, childIds, ct)];
        }

        // Las aristas de la rama se van con ella, en los dos sentidos: Restore no las trae.
        foreach (var edge in await dependencies.ListTouchingAsync(tenantId, deletedIds, ct))
            dependencies.Remove(edge);
        return deletedIds;
    }

    private async Task MutateParentAsync(
        Guid tenantId,
        Guid? parentTaskId,
        Action<TaskItem> mutate,
        CancellationToken ct
    )
    {
        if (parentTaskId is not { } id)
            return;

        var parentResult = await tasks.GetByIdAsync(tenantId, id, ct);
        if (parentResult.IsSuccess)
            mutate(parentResult.Value);
    }
}
