using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using TaxVision.Tasks.Application.Dependencies.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using Wolverine;

namespace TaxVision.Tasks.Application.Dependencies;

public sealed class TaskUnblockingService(
    ITaskRepository tasks,
    ITaskDependencyRepository dependencies,
    IMessageBus bus,
    ICorrelationContext correlation
) : ITaskUnblockingService
{
    public async Task ApplyPredecessorClosedAsync(Guid tenantId, Guid predecessorTaskId, CancellationToken ct = default)
    {
        var successors = await LoadSuccessorsAsync(tenantId, predecessorTaskId, ct);
        var nowUtc = DateTime.UtcNow;

        foreach (var successor in successors)
        {
            // Sin esto, un mensaje reprocesado vuelve a avisar del mismo desbloqueo.
            var wasBlocked = successor.IsBlocked;
            successor.RegisterBlockerResolved(nowUtc);

            if (wasBlocked && successor.OpenBlockerCount == 0)
                await PublishUnblockedAsync(successor);
        }
    }

    public async Task ApplyPredecessorReopenedAsync(
        Guid tenantId,
        Guid predecessorTaskId,
        CancellationToken ct = default
    )
    {
        var successors = await LoadSuccessorsAsync(tenantId, predecessorTaskId, ct);
        foreach (var successor in successors)
            successor.RegisterBlockerReopened();
    }

    private async Task<IReadOnlyList<TaskItem>> LoadSuccessorsAsync(
        Guid tenantId,
        Guid predecessorTaskId,
        CancellationToken ct
    )
    {
        var successorIds = await dependencies.ListSuccessorIdsAsync(tenantId, predecessorTaskId, ct);
        return successorIds.Count == 0 ? [] : await tasks.ListByIdsAsync(tenantId, successorIds, ct);
    }

    private async Task PublishUnblockedAsync(TaskItem successor)
    {
        await bus.PublishAsync(
            new TaskUnblockedIntegrationEvent
            {
                TenantId = successor.TenantId,
                CorrelationId = correlation.CorrelationId,
                TaskId = successor.Id,
                Title = successor.Title.Value,
                AssigneeUserId = successor.AssigneeUserId,
            }
        );
    }
}
