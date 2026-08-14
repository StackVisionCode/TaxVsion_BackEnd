using TaxVision.Tasks.Application.Hierarchy.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Hierarchy;

public sealed class TaskHierarchyService(ITaskRepository tasks) : ITaskHierarchyService
{
    public Task ApplyChildClosedAsync(Guid tenantId, Guid? parentTaskId, CancellationToken ct = default) =>
        MutateParentAsync(tenantId, parentTaskId, parent => parent.RegisterSubtaskClosed(), ct);

    public Task ApplyChildReopenedAsync(Guid tenantId, Guid? parentTaskId, CancellationToken ct = default) =>
        MutateParentAsync(tenantId, parentTaskId, parent => parent.RegisterSubtaskReopened(), ct);

    public async Task DeleteWithDescendantsAsync(
        Guid tenantId,
        Guid taskId,
        Guid byUserId,
        CancellationToken ct = default
    )
    {
        var rootResult = await tasks.GetByIdAsync(tenantId, taskId, ct);
        if (rootResult.IsFailure)
            return;

        var nowUtc = DateTime.UtcNow;
        var root = rootResult.Value;

        // El padre pierde un hijo abierto sólo si la raíz del borrado lo era.
        await ApplyChildClosedAsync(tenantId, root.ParentTaskId, ct);

        var level = new List<TaskItem> { root };
        while (level.Count > 0)
        {
            foreach (var task in level)
            {
                task.Delete(byUserId, nowUtc);
                tasks.Remove(task);
            }

            var childIds = await tasks.ListChildIdsAsync(tenantId, [.. level.Select(t => t.Id)], ct);
            level = childIds.Count == 0 ? [] : [.. await tasks.ListByIdsAsync(tenantId, childIds, ct)];
        }
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
