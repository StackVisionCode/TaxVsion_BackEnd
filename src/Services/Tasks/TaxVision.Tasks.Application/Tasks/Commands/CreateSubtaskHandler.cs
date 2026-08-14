using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using Wolverine;

namespace TaxVision.Tasks.Application.Tasks.Commands;

/// <summary>
/// Sin <c>CustomerId</c> ni <c>TaxYear</c>: la subtarea hereda la referencia del padre. Dejar que el
/// llamador la mandara permitiría un hijo apuntando a otro cliente que el padre.
/// </summary>
public sealed record CreateSubtaskCommand(
    Guid TenantId,
    Guid ParentTaskId,
    Guid ByUserId,
    bool HasManageAll,
    string? Title,
    string? Description,
    TaskPriority Priority,
    Guid? AssigneeUserId,
    DateTime? DueAtUtc,
    string? DueTimeZoneId,
    bool DueIsStatutory,
    decimal? EstimatedHours
);

public static class CreateSubtaskHandler
{
    public static async Task<Result<TaskResponse>> Handle(
        CreateSubtaskCommand command,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var parent = await tasks.GetByIdAsync(command.TenantId, command.ParentTaskId, ct);
        if (parent.IsFailure)
            return Result.Failure<TaskResponse>(parent.Error);

        if (!TaskAccessPolicy.CanMutate(parent.Value, command.ByUserId, command.HasManageAll))
            return Result.Failure<TaskResponse>(TaskErrors.Forbidden);

        var draft = BuildDraft(command);
        if (draft.IsFailure)
            return Result.Failure<TaskResponse>(draft.Error);

        var created = Build(command, parent.Value, draft.Value);
        if (created.IsFailure)
            return Result.Failure<TaskResponse>(created.Error);

        tasks.Add(created.Value);
        await bus.PublishAsync(TaskCreatedEventFactory.From(created.Value, correlation.CorrelationId));
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(TaskResponse.From(created.Value));
    }

    private static Result<TaskItem> Build(CreateSubtaskCommand command, TaskItem parent, TaskDraft draft) =>
        TaskItem.CreateSubtask(
            parent,
            command.ByUserId,
            draft.Title,
            draft.Description,
            command.Priority,
            draft.Due,
            draft.Estimated,
            command.AssigneeUserId,
            DateTime.UtcNow
        );

    private static Result<TaskDraft> BuildDraft(CreateSubtaskCommand command) =>
        TaskDraft.From(
            command.Title,
            command.Description,
            command.DueAtUtc,
            command.DueTimeZoneId,
            command.DueIsStatutory,
            command.EstimatedHours,
            null,
            null
        );
}
