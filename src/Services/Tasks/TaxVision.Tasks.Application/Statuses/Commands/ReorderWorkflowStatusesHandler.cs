using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Statuses.Abstractions;
using TaxVision.Tasks.Domain.Statuses;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Application.Statuses.Commands;

public sealed record ReorderWorkflowStatusesCommand(
    Guid TenantId,
    TaskItemStatus Category,
    IReadOnlyList<string> Codes
);

/// <summary>Orden dentro de una etapa; el orden entre etapas es fijo.</summary>
public static class ReorderWorkflowStatusesHandler
{
    public static async Task<Result> Handle(
        ReorderWorkflowStatusesCommand command,
        IWorkflowStatusRepository statuses,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var rows = (await statuses.ListAsync(command.TenantId, false, ct))
            .Where(r => r.Category == command.Category)
            .ToDictionary(r => r.Code.Value);
        for (var position = 0; position < command.Codes.Count; position++)
        {
            if (rows.TryGetValue(command.Codes[position], out var row))
                row.MoveTo((position + 1) * 10);
            else if (!IsSystemCodeOf(command.Codes[position], command.Category))
                return Result.Failure(TaskErrors.Status.NotFound);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static bool IsSystemCodeOf(string code, TaskItemStatus category) =>
        TaskLabelCode.SystemCodeOf(category).Value == code;
}
