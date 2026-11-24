using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Statuses.Abstractions;
using TaxVision.Tasks.Domain.Statuses;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Application.Statuses.Commands;

public sealed record ArchiveWorkflowStatusCommand(Guid TenantId, Guid ByUserId, string Code, string? MoveOpenTasksTo);

/// <summary>
/// Sin tareas abiertas, directo. Con tareas abiertas exige destino de la misma etapa y las mueve en un solo
/// <c>UPDATE</c>; las cerradas conservan el estado como dato histórico.
/// </summary>
public static class ArchiveWorkflowStatusHandler
{
    public static async Task<Result> Handle(
        ArchiveWorkflowStatusCommand command,
        IWorkflowStatusRepository statuses,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var code = TaskLabelCode.Create(command.Code);
        var status = code.IsSuccess ? await statuses.GetByCodeAsync(command.TenantId, code.Value, ct) : null;
        if (status is null)
            return Result.Failure(TaskErrors.Status.NotFound);

        var moved = await MoveOpenTasksAsync(command, status, statuses, ct);
        if (moved.IsFailure)
            return moved;

        var archived = status.Archive(command.ByUserId, DateTime.UtcNow);
        if (archived.IsFailure)
            return archived;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static async Task<Result> MoveOpenTasksAsync(
        ArchiveWorkflowStatusCommand command,
        WorkflowStatus status,
        IWorkflowStatusRepository statuses,
        CancellationToken ct
    )
    {
        var open = await statuses.CountOpenTasksAsync(command.TenantId, status.Id, ct);
        if (open == 0)
            return Result.Success();
        if (string.IsNullOrWhiteSpace(command.MoveOpenTasksTo))
            return Result.Failure(TaskErrors.Status.InUse(open));

        var target = await WorkflowStatusCatalog.ResolveAsync(
            statuses,
            command.TenantId,
            command.MoveOpenTasksTo,
            command.ByUserId,
            DateTime.UtcNow,
            ct
        );
        if (target.IsFailure)
            return Result.Failure(target.Error);
        if (target.Value.Category != status.Category || target.Value.Id == status.Id)
            return Result.Failure(TaskErrors.Status.CategoryLocked);

        await statuses.MoveOpenTasksAsync(command.TenantId, status.Id, target.Value, ct);
        return Result.Success();
    }
}
