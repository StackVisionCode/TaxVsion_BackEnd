using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Labels.Abstractions;
using TaxVision.Tasks.Domain.Labels;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Application.Labels.Commands;

public sealed record CreateTaskLabelCommand(
    Guid TenantId,
    string? Code,
    string? DisplayName,
    string? Color,
    TaskItemStatus MapsToStatus,
    int SortOrder
);

public static class CreateTaskLabelHandler
{
    public static async Task<Result<TaskLabelResponse>> Handle(
        CreateTaskLabelCommand command,
        ITaskLabelRepository labels,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var code = TaskLabelCode.Create(command.Code);
        if (code.IsFailure)
            return Result.Failure<TaskLabelResponse>(code.Error);

        var color = LabelColor.Create(command.Color);
        if (color.IsFailure)
            return Result.Failure<TaskLabelResponse>(color.Error);

        if (await labels.CodeExistsAsync(command.TenantId, code.Value, null, ct))
            return Result.Failure<TaskLabelResponse>(TaskErrors.Label.CodeTaken);

        var created = TaskLabel.Create(
            command.TenantId,
            code.Value,
            command.DisplayName,
            color.Value,
            command.MapsToStatus,
            command.SortOrder
        );
        if (created.IsFailure)
            return Result.Failure<TaskLabelResponse>(created.Error);

        labels.Add(created.Value);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(TaskLabelResponse.From(created.Value));
    }
}
