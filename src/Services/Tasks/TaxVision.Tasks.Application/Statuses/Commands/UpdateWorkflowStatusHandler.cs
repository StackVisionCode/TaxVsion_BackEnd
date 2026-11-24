using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Statuses.Abstractions;
using TaxVision.Tasks.Domain.Statuses;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Application.Statuses.Commands;

public sealed record UpdateWorkflowStatusCommand(
    Guid TenantId,
    Guid ByUserId,
    string Code,
    string? DisplayName,
    string? Color,
    string? Description,
    string? RowVersion
);

/// <summary>
/// Nombre, color y descripción. Un estado del sistema sin fila se materializa al personalizarlo. Con
/// <c>RowVersion</c> de otra edición, el guardado da 409.
/// </summary>
public static class UpdateWorkflowStatusHandler
{
    public static async Task<Result<WorkflowStatusResponse>> Handle(
        UpdateWorkflowStatusCommand command,
        IWorkflowStatusRepository statuses,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var color = LabelColor.Create(command.Color);
        if (color.IsFailure)
            return Result.Failure<WorkflowStatusResponse>(color.Error);

        var found = await FindOrMaterializeAsync(command, color.Value, statuses, ct);
        if (found.IsFailure)
            return Result.Failure<WorkflowStatusResponse>(found.Error);

        var status = found.Value;
        if (command.DisplayName is { } name && await statuses.NameTakenAsync(command.TenantId, name, status.Id, ct))
            return Result.Failure<WorkflowStatusResponse>(TaskErrors.Status.NameTaken);

        var changed = Apply(command, color.Value, status);
        if (changed.IsFailure)
            return Result.Failure<WorkflowStatusResponse>(changed.Error);

        if (command.RowVersion is { Length: > 0 } version && status.RowVersion.Length > 0)
            statuses.ExpectVersion(status, Convert.FromBase64String(version));
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(WorkflowStatusResponse.From(status));
    }

    private static Result Apply(UpdateWorkflowStatusCommand command, LabelColor color, WorkflowStatus status)
    {
        var now = DateTime.UtcNow;
        var renamed = status.Rename(command.DisplayName, command.ByUserId, now);
        if (renamed.IsFailure)
            return renamed;

        status.ChangeColor(color, command.ByUserId, now);
        return status.Describe(command.Description, command.ByUserId, now);
    }

    private static async Task<Result<WorkflowStatus>> FindOrMaterializeAsync(
        UpdateWorkflowStatusCommand command,
        LabelColor color,
        IWorkflowStatusRepository statuses,
        CancellationToken ct
    )
    {
        var code = TaskLabelCode.Create(command.Code);
        if (code.IsFailure)
            return Result.Failure<WorkflowStatus>(TaskErrors.Status.NotFound);

        var row = await statuses.GetByCodeAsync(command.TenantId, code.Value, ct);
        if (row is not null)
            return Result.Success(row);
        if (!TaskLabelCode.IsReserved(code.Value))
            return Result.Failure<WorkflowStatus>(TaskErrors.Status.NotFound);

        var category = WorkflowStatusCatalog.Categories.First(c => TaskLabelCode.SystemCodeOf(c) == code.Value);
        var rows = await statuses.ListAsync(command.TenantId, false, ct);
        var isDefault = !rows.Any(r => r.Category == category && r.IsDefault);
        var created = WorkflowStatus.CustomizeSystem(
            command.TenantId,
            category,
            command.DisplayName,
            color,
            isDefault,
            command.ByUserId,
            DateTime.UtcNow
        );
        if (created.IsSuccess)
            statuses.Add(created.Value);
        return created;
    }
}
