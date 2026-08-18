using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Dependencies.Abstractions;
using TaxVision.Tasks.Application.Hierarchy.Abstractions;
using TaxVision.Tasks.Application.Reminders;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using Wolverine;

namespace TaxVision.Tasks.Application.Tasks.Commands;

public sealed record DeleteTaskCommand(Guid TenantId, Guid TaskId, Guid ByUserId, bool HasManageAll);

/// <summary>
/// Borrar arrastra el subárbol entero y libera a las sucesoras: una tarea que ya no existe no puede
/// seguir bloqueando a otra.
/// </summary>
public static class DeleteTaskHandler
{
    public static async Task<Result> Handle(
        DeleteTaskCommand command,
        ITaskRepository tasks,
        ITaskHierarchyService hierarchy,
        ITaskUnblockingService unblocking,
        ITaskSeriesMaterializer seriesMaterializer,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var found = await tasks.GetByIdAsync(command.TenantId, command.TaskId, ct);
        if (found.IsFailure)
            return Result.Failure(found.Error);

        if (!TaskAccessPolicy.CanMutate(found.Value, command.ByUserId, command.HasManageAll))
            return Result.Failure(TaskErrors.Forbidden);

        var wasOpen = found.Value.Status is not (TaskItemStatus.Completed or TaskItemStatus.Cancelled);
        if (wasOpen)
            await unblocking.ApplyPredecessorClosedAsync(command.TenantId, command.TaskId, ct);

        // Borrar la instancia abierta libera la serie y siembra la siguiente: para cortar la serie hay
        // que pausarla o terminarla, no borrar su ocurrencia.
        var next = await seriesMaterializer.ApplyInstanceClosedAsync(found.Value, null, ct);
        if (next is not null)
            await TaskDueReminder.PublishIfDueAsync(next, bus, correlation);
        await bus.PublishAsync(TaskReminderContracts.Closed(found.Value, "deleted", correlation.CorrelationId));

        await hierarchy.DeleteWithDescendantsAsync(command.TenantId, command.TaskId, command.ByUserId, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
