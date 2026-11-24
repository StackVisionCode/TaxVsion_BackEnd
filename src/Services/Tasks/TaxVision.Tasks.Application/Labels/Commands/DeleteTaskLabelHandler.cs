using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Statuses.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Labels.Commands;

/// <summary>
/// Borrado real solo si ninguna tarea lo tuvo nunca; si lo tuvo, se archiva (las cerradas lo conservan). Con
/// tareas abiertas en ese estado, hay que archivarlo eligiendo destino.
/// </summary>
public sealed record DeleteTaskLabelCommand(Guid TenantId, Guid LabelId, Guid ByUserId);

public static class DeleteTaskLabelHandler
{
    public static async Task<Result> Handle(
        DeleteTaskLabelCommand command,
        IWorkflowStatusRepository labels,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await labels.GetByIdAsync(command.TenantId, command.LabelId, ct);
        if (found.IsFailure)
            return Result.Failure(found.Error);

        var status = found.Value;
        var open = await labels.CountOpenTasksAsync(command.TenantId, status.Id, ct);
        if (open > 0)
            return Result.Failure(TaskErrors.Status.InUse(open));

        if (await labels.IsUsedAsync(command.TenantId, status.Id, ct))
        {
            var archived = status.Archive(command.ByUserId, DateTime.UtcNow);
            if (archived.IsFailure)
                return archived;
        }
        else
            labels.Remove(status);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
