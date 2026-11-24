using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Statuses.Abstractions;
using TaxVision.Tasks.Domain.Statuses;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Application.Statuses.Commands;

public sealed record CreateWorkflowStatusCommand(
    Guid TenantId,
    Guid ByUserId,
    string? DisplayName,
    string? Color,
    TaskItemStatus Category,
    string? Description
);

/// <summary>El código sale del nombre; solo en etapas abiertas; nombre único entre los activos.</summary>
public static class CreateWorkflowStatusHandler
{
    public static async Task<Result<WorkflowStatusResponse>> Handle(
        CreateWorkflowStatusCommand command,
        IWorkflowStatusRepository statuses,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var color = LabelColor.Create(command.Color);
        if (color.IsFailure)
            return Result.Failure<WorkflowStatusResponse>(color.Error);

        var allowed = await CheckRoomAsync(command, statuses, ct);
        if (allowed.IsFailure)
            return Result.Failure<WorkflowStatusResponse>(allowed.Error);

        var created = await NewStatusAsync(command, color.Value, statuses, ct);
        if (created.IsFailure)
            return Result.Failure<WorkflowStatusResponse>(created.Error);

        statuses.Add(created.Value);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(WorkflowStatusResponse.From(created.Value));
    }

    private static async Task<Result> CheckRoomAsync(
        CreateWorkflowStatusCommand command,
        IWorkflowStatusRepository statuses,
        CancellationToken ct
    )
    {
        if (await statuses.CountActiveAsync(command.TenantId, ct) >= WorkflowStatus.MaxActivePerTenant)
            return Result.Failure(TaskErrors.Status.LimitReached);

        return command.DisplayName is { } name && await statuses.NameTakenAsync(command.TenantId, name, null, ct)
            ? Result.Failure(TaskErrors.Status.NameTaken)
            : Result.Success();
    }

    private static async Task<Result<WorkflowStatus>> NewStatusAsync(
        CreateWorkflowStatusCommand command,
        LabelColor color,
        IWorkflowStatusRepository statuses,
        CancellationToken ct
    )
    {
        var rows = await statuses.ListAsync(command.TenantId, true, ct);
        var code = TaskLabelCode.FromDisplayName(
            command.DisplayName,
            await statuses.ListCodesAsync(command.TenantId, ct)
        );
        var lastInCategory = rows.Where(r => r.Category == command.Category)
            .Select(r => r.SortOrder)
            .DefaultIfEmpty(0)
            .Max();
        return WorkflowStatus.CreateCustom(
            command.TenantId,
            code,
            command.DisplayName,
            color,
            command.Category,
            lastInCategory + 10,
            command.Description,
            command.ByUserId,
            DateTime.UtcNow
        );
    }
}
