using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Common.Abstractions;
using TaxVision.Tasks.Application.Dependencies.Abstractions;
using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Dependencies;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Dependencies.Commands;

public sealed record AddDependencyCommand(
    Guid TenantId,
    Guid TaskId,
    Guid DependsOnTaskId,
    Guid ByUserId,
    bool HasManageAll = false
);

/// <summary>
/// Sin el <c>UPDLOCK</c>, dos requests que crean A→B y B→A validan contra un grafo sin ciclo y pasan
/// los dos.
/// </summary>
public static class AddDependencyHandler
{
    public static async Task<Result> Handle(
        AddDependencyCommand command,
        ITaskRepository tasks,
        ITaskDependencyRepository dependencies,
        ITransactionalScope scope,
        IUnitOfWork unitOfWork,
        ITaskMetrics metrics,
        CancellationToken ct
    )
    {
        var dependencyResult = NewEdge(command);
        if (dependencyResult.IsFailure)
            return Result.Failure(dependencyResult.Error);

        await using var transaction = await scope.BeginAsync(ct);
        await dependencies.LockTenantEdgesAsync(command.TenantId, ct);

        var endpoints = await LoadAuthorizedEndpointsAsync(command, tasks, ct);
        if (endpoints.IsFailure)
            return Result.Failure(endpoints.Error);
        var (successor, predecessor) = endpoints.Value;

        var check = await EnsureEdgeIsAllowedAsync(command, tasks, dependencies, ct);
        if (check.IsFailure)
        {
            if (check.Error == TaskErrors.Dependency.Cycle)
                metrics.RecordDependencyCycleRejected();

            return check;
        }

        dependencies.Add(dependencyResult.Value);

        // Sumar por una predecesora ya cerrada dejaría a la sucesora trabada para siempre.
        if (!IsClosed(predecessor))
            successor.RegisterBlockerAdded();

        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// Las dos tareas del borde, ya validadas. A1 — la que cambia es la SUCESORA: gana un bloqueador y
    /// puede quedar trabada. Leer la predecesora es de toda la firma, así que solo se valida quién puede
    /// mutar la sucesora.
    /// </summary>
    private static async Task<Result<(TaskItem Successor, TaskItem Predecessor)>> LoadAuthorizedEndpointsAsync(
        AddDependencyCommand command,
        ITaskRepository tasks,
        CancellationToken ct
    )
    {
        var successor = await tasks.GetByIdAsync(command.TenantId, command.TaskId, ct);
        var predecessor = await tasks.GetByIdAsync(command.TenantId, command.DependsOnTaskId, ct);
        if (successor.IsFailure || predecessor.IsFailure)
            return Result.Failure<(TaskItem, TaskItem)>(TaskErrors.Dependency.CrossTenant);

        if (!TaskAccessPolicy.CanMutate(successor.Value, command.ByUserId, command.HasManageAll))
            return Result.Failure<(TaskItem, TaskItem)>(TaskErrors.Forbidden);

        return Result.Success((successor.Value, predecessor.Value));
    }

    private static Result<TaskDependency> NewEdge(AddDependencyCommand command) =>
        TaskDependency.Create(
            command.TenantId,
            command.TaskId,
            command.DependsOnTaskId,
            command.ByUserId,
            DateTime.UtcNow
        );

    /// <summary>Lo barato antes que la consulta recursiva.</summary>
    private static async Task<Result> EnsureEdgeIsAllowedAsync(
        AddDependencyCommand command,
        ITaskRepository tasks,
        ITaskDependencyRepository dependencies,
        CancellationToken ct
    )
    {
        var existing = await dependencies.GetAsync(command.TenantId, command.TaskId, command.DependsOnTaskId, ct);
        if (existing is not null)
            return Result.Failure(TaskErrors.Dependency.Duplicate);

        var ancestors = await tasks.GetAncestorIdsAsync(command.TenantId, command.DependsOnTaskId, ct);
        foreach (var ancestorId in ancestors)
        {
            if (ancestorId == command.TaskId)
                return Result.Failure(TaskErrors.Dependency.AncestorOfSelf);
        }

        var edges = await dependencies.LoadUpstreamGraphAsync(command.TenantId, command.DependsOnTaskId, ct);
        return TaskDependencyGraph.EnsureNoCycle(command.TaskId, command.DependsOnTaskId, edges);
    }

    private static bool IsClosed(TaskItem task) => task.Status is TaskItemStatus.Completed or TaskItemStatus.Cancelled;
}
