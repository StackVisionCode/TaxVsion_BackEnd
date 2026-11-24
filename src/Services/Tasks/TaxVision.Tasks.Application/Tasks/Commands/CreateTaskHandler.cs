using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Common.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

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
    decimal? EstimatedHours,
    DateOnly? DueOn = null,
    bool CanAssignOthers = true,
    bool CanViewAllCustomers = true
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
        ITaskMetrics metrics,
        IOfficeTimeZone zones,
        ITaskCustomerAccess customers,
        CancellationToken ct
    )
    {
        var allowed = await CheckAccessAsync(command, customers, ct);
        if (allowed.IsFailure)
            return Result.Failure<TaskResponse>(allowed.Error);

        var officeZone = command.DueOn is null ? null : await zones.ResolveAsync(command.TenantId, ct);
        var draft = BuildDraft(command, officeZone);
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

        // Los anuncios del alta los devuelve TaskCreatedDomainEventHandler.
        tasks.Add(created.Value);
        await unitOfWork.SaveChangesAsync(ct);
        metrics.RecordCreated(created.Value.Reference.CustomerId is not null);

        return Result.Success(TaskResponse.From(created.Value));
    }

    private static Result<TaskDraft> BuildDraft(
        CreateTaskCommand command,
        BuildingBlocks.TimeZones.ResolvedTimeZone? officeZone
    ) =>
        TaskDraft.From(
            command.Title,
            command.Description,
            command.DueAtUtc,
            command.DueTimeZoneId,
            command.DueIsStatutory,
            command.EstimatedHours,
            command.CustomerId,
            command.TaxYear,
            command.DueOn,
            officeZone
        );

    /// <summary>Darle la tarea a otro pide tasks.assign; ligarla a un cliente, poder verlo.</summary>
    private static async Task<Result> CheckAccessAsync(
        CreateTaskCommand command,
        ITaskCustomerAccess customers,
        CancellationToken ct
    )
    {
        if (!command.CanAssignOthers && command.AssigneeUserId is { } other && other != command.ByUserId)
            return Result.Failure(TaskErrors.Forbidden);
        if (
            command.CustomerId is { } customer
            && !await customers.CanLinkAsync(
                command.TenantId,
                command.ByUserId,
                command.CanViewAllCustomers,
                customer,
                ct
            )
        )
            return Result.Failure(TaskErrors.Reference.CustomerNotVisible);
        return Result.Success();
    }
}
