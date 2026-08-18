using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Application.Tasks.Commands;

public sealed record UpdateTaskDetailsCommand(
    Guid TenantId,
    Guid TaskId,
    Guid ByUserId,
    bool HasManageAll,
    string? Title,
    string? Description
);

/// <summary>
/// Título y descripción juntos: es un solo formulario y separarlos obligaría al front a dos llamadas
/// para guardar una edición.
/// </summary>
public static class UpdateTaskDetailsHandler
{
    public static async Task<Result<TaskResponse>> Handle(
        UpdateTaskDetailsCommand command,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var title = TaskTitle.Create(command.Title);
        if (title.IsFailure)
            return Result.Failure<TaskResponse>(title.Error);

        var found = await tasks.GetByIdAsync(command.TenantId, command.TaskId, ct);
        if (found.IsFailure)
            return Result.Failure<TaskResponse>(found.Error);

        if (!TaskAccessPolicy.CanMutate(found.Value, command.ByUserId, command.HasManageAll))
            return Result.Failure<TaskResponse>(TaskErrors.Forbidden);

        var applied = Apply(found.Value, title.Value, command.Description);
        if (applied.IsFailure)
            return Result.Failure<TaskResponse>(applied.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(TaskResponse.From(found.Value));
    }

    /// <summary>Descripción vacía es «quitarla», no un valor inválido.</summary>
    private static Result Apply(TaskItem task, TaskTitle title, string? description)
    {
        var renamed = task.ChangeTitle(title);
        if (renamed.IsFailure)
            return renamed;

        if (string.IsNullOrWhiteSpace(description))
            return task.ChangeDescription(null);

        var parsed = TaskDescription.Create(description);
        return parsed.IsFailure ? Result.Failure(parsed.Error) : task.ChangeDescription(parsed.Value);
    }
}
