using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Timers.Commands;

public sealed record EditTaskTimeEntryCommand(
    Guid TenantId,
    Guid TaskId,
    Guid EntryId,
    Guid ByUserId,
    bool HasManageAll,
    int Minutes,
    bool IsBillable,
    string? Note
);

/// <summary>Corrige un registro cerrado: su autor, o quien supervisa toda la oficina.</summary>
public static class EditTaskTimeEntryHandler
{
    public static async Task<Result<TaskTimerResponse>> Handle(
        EditTaskTimeEntryCommand command,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await tasks.GetByIdWithTimersAsync(command.TenantId, command.TaskId, ct);
        if (found.IsFailure)
            return Result.Failure<TaskTimerResponse>(found.Error);

        var edited = found.Value.EditTimeEntry(
            command.EntryId,
            command.ByUserId,
            command.HasManageAll,
            command.Minutes,
            command.IsBillable,
            command.Note,
            DateTime.UtcNow
        );
        if (edited.IsFailure)
            return Result.Failure<TaskTimerResponse>(edited.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(TaskTimerResponse.From(edited.Value));
    }
}
