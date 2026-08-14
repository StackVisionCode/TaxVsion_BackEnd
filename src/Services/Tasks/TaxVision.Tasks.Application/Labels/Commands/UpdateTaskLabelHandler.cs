using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Labels.Abstractions;
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
        ITaskLabelRepository labels,
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

        var renamed = found.Value.Rename(command.DisplayName, color.Value, command.MapsToStatus, command.SortOrder);
        if (renamed.IsFailure)
            return Result.Failure<TaskLabelResponse>(renamed.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(TaskLabelResponse.From(found.Value));
    }
}
