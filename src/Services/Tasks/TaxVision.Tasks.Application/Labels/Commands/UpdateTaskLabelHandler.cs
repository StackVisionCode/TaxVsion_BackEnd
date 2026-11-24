using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Statuses.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Application.Labels.Commands;

/// <summary>Sin <c>Code</c>: renombrar el código rompería lo que el front tiene guardado.</summary>
public sealed record UpdateTaskLabelCommand(
    Guid TenantId,
    Guid LabelId,
    string? DisplayName,
    string? Color,
    TaskItemStatus MapsToStatus,
    int SortOrder
);

public static class UpdateTaskLabelHandler
{
    public static async Task<Result<TaskLabelResponse>> Handle(
        UpdateTaskLabelCommand command,
        IWorkflowStatusRepository labels,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var color = LabelColor.Create(command.Color);
        if (color.IsFailure)
            return Result.Failure<TaskLabelResponse>(color.Error);

        var found = await labels.GetByIdAsync(command.TenantId, command.LabelId, ct);
        if (found.IsFailure)
            return Result.Failure<TaskLabelResponse>(found.Error);

        // Un estado que ya tuvo tareas no cambia de etapa: cambiaría el significado de esas tareas.
        if (
            command.MapsToStatus != found.Value.Category
            && await labels.IsUsedAsync(command.TenantId, found.Value.Id, ct)
        )
            return Result.Failure<TaskLabelResponse>(TaskErrors.Status.CategoryLocked);

        var renamed = found.Value.Rename(command.DisplayName, color.Value, command.MapsToStatus, command.SortOrder);
        if (renamed.IsFailure)
            return Result.Failure<TaskLabelResponse>(renamed.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(TaskLabelResponse.From(found.Value));
    }
}
