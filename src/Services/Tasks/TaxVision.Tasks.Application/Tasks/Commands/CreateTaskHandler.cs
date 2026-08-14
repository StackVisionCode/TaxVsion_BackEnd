using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Reminders;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using Wolverine;

namespace TaxVision.Tasks.Application.Tasks.Commands;

public sealed record CreateTaskCommand(
    Guid TenantId,
    Guid ByUserId,
    string? Title,
    string? Description,
    TaskPriority Priority,
    Guid? AssigneeUserId,
    Guid? CustomerId,
    int? TaxYear,
    DateTime? DueAtUtc,
    string? DueTimeZoneId,
    bool DueIsStatutory,
    decimal? EstimatedHours
);

/// <summary>
/// El <c>CustomerId</c> no se valida contra Customer: es un id opaco y la proyección local puede ir
/// atrasada. Bloquear el alta por lag convertiría una consistencia eventual en un error de usuario.
/// </summary>
public static class CreateTaskHandler
{
    public static async Task<Result<TaskResponse>> Handle(
        CreateTaskCommand command,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ITaskMetrics metrics,
        CancellationToken ct
    )
    {
        var draft = BuildDraft(command);
        if (draft.IsFailure)
            return Result.Failure<TaskResponse>(draft.Error);

        var created = TaskItem.Create(
            command.TenantId,
            command.ByUserId,
            draft.Value.Title,
            draft.Value.Description,
            command.Priority,
            draft.Value.Reference,
            draft.Value.Due,
            draft.Value.Estimated,
            command.AssigneeUserId,
            DateTime.UtcNow
        );
        if (created.IsFailure)
            return Result.Failure<TaskResponse>(created.Error);

        tasks.Add(created.Value);
        await bus.PublishAsync(TaskCreatedEventFactory.From(created.Value, correlation.CorrelationId));
        await TaskDueReminder.PublishIfDueAsync(created.Value, bus, correlation);
        await unitOfWork.SaveChangesAsync(ct);
        metrics.RecordCreated(created.Value.Reference.CustomerId is not null);

        return Result.Success(TaskResponse.From(created.Value));
    }

    private static Result<TaskDraft> BuildDraft(CreateTaskCommand command) =>
        TaskDraft.From(
            command.Title,
            command.Description,
            command.DueAtUtc,
            command.DueTimeZoneId,
            command.DueIsStatutory,
            command.EstimatedHours,
            command.CustomerId,
            command.TaxYear
        );
}
