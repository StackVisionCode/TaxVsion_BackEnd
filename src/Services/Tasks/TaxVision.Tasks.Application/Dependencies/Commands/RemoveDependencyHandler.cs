using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Common.Abstractions;
using TaxVision.Tasks.Application.Dependencies.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Dependencies.Commands;

public sealed record RemoveDependencyCommand(Guid TenantId, Guid TaskId, Guid DependsOnTaskId);

/// <summary>Mismo cerrojo que al agregar: sin él esto se cruza con la cascada de un completado.</summary>
public static class RemoveDependencyHandler
{
    public static async Task<Result> Handle(
        RemoveDependencyCommand command,
        ITaskRepository tasks,
        ITaskDependencyRepository dependencies,
        ITransactionalScope scope,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        await using var transaction = await scope.BeginAsync(ct);
        await dependencies.LockTenantEdgesAsync(command.TenantId, ct);

        var dependency = await dependencies.GetAsync(command.TenantId, command.TaskId, command.DependsOnTaskId, ct);
        if (dependency is null)
            return Result.Failure(TaskErrors.Dependency.NotFound);

        var predecessorResult = await tasks.GetByIdAsync(command.TenantId, command.DependsOnTaskId, ct);
        var successorResult = await tasks.GetByIdAsync(command.TenantId, command.TaskId, ct);

        dependencies.Remove(dependency);

        // Si la predecesora ya estaba cerrada nunca sumó, así que tampoco resta.
        if (successorResult.IsSuccess && predecessorResult.IsSuccess && !IsClosed(predecessorResult.Value))
            successorResult.Value.RegisterBlockerResolved(DateTime.UtcNow);

        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Success();
    }

    private static bool IsClosed(TaskItem task) => task.Status is TaskItemStatus.Completed or TaskItemStatus.Cancelled;
}
