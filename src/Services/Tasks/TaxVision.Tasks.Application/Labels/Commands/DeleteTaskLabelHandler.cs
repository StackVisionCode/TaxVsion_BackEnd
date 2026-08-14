using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Labels.Abstractions;

namespace TaxVision.Tasks.Application.Labels.Commands;

/// <summary>
/// Borrado real, no archivado: ninguna tarea guarda el id del label — el motor lee
/// <c>TaskItemStatus</c>, así que quitarlo no deja referencias colgando.
/// </summary>
public sealed record DeleteTaskLabelCommand(Guid TenantId, Guid LabelId);

public static class DeleteTaskLabelHandler
{
    public static async Task<Result> Handle(
        DeleteTaskLabelCommand command,
        ITaskLabelRepository labels,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await labels.GetByIdAsync(command.TenantId, command.LabelId, ct);
        if (found.IsFailure)
            return Result.Failure(found.Error);

        labels.Remove(found.Value);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
