using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Common.Abstractions;
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
    decimal? EstimatedHours,
    DateOnly? DueOn = null
);

public static class CreateSubtaskHandler
{
    public static async Task<Result<TaskResponse>> Handle(
        CreateSubtaskCommand command,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        IOfficeTimeZone zones,
        CancellationToken ct
    )
    {
        var parent = await tasks.GetByIdAsync(command.TenantId, command.ParentTaskId, ct);
        if (parent.IsFailure)
            return Result.Failure<TaskResponse>(parent.Error);

        if (!TaskAccessPolicy.CanMutate(parent.Value, command.ByUserId, command.HasManageAll))
            return Result.Failure<TaskResponse>(TaskErrors.Forbidden);

        var officeZone = command.DueOn is null ? null : await zones.ResolveAsync(command.TenantId, ct);
        var draft = BuildDraft(command, officeZone);
        if (draft.IsFailure)
            return Result.Failure<TaskResponse>(draft.Error);

        var created = Build(command, parent.Value, draft.Value);
        if (created.IsFailure)
            return Result.Failure<TaskResponse>(created.Error);

        // Los anuncios del alta los devuelve TaskCreatedDomainEventHandler.
        tasks.Add(created.Value);
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

    private static Result<TaskDraft> BuildDraft(
        CreateSubtaskCommand command,
        BuildingBlocks.TimeZones.ResolvedTimeZone? officeZone
    ) =>
        TaskDraft.From(
            command.Title,
            command.Description,
            command.DueAtUtc,
            command.DueTimeZoneId,
            command.DueIsStatutory,
            command.EstimatedHours,
            null,
            null,
            command.DueOn,
            officeZone
        );
}
