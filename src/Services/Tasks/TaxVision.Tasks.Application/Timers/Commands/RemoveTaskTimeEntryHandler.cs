using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Timers.Commands;

public sealed record RemoveTaskTimeEntryCommand(
    Guid TenantId,
    Guid TaskId,
    Guid EntryId,
    Guid ByUserId,
    bool HasManageAll
);

/// <summary>Borra un registro cerrado y resta sus horas del total.</summary>
public static class RemoveTaskTimeEntryHandler
{
    public static async Task<Result> Handle(
        RemoveTaskTimeEntryCommand command,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await tasks.GetByIdWithTimersAsync(command.TenantId, command.TaskId, ct);
        if (found.IsFailure)
            return Result.Failure(found.Error);

        var removed = found.Value.RemoveTimeEntry(
            command.EntryId,
            command.ByUserId,
            command.HasManageAll,
            DateTime.UtcNow
        );
        if (removed.IsFailure)
            return removed;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
