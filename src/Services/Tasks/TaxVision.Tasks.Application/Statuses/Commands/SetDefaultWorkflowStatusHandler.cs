using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Statuses.Abstractions;
using TaxVision.Tasks.Domain.Statuses;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Application.Statuses.Commands;

public sealed record SetDefaultWorkflowStatusCommand(Guid TenantId, Guid ByUserId, string Code);

/// <summary>
/// El estado que recibe una tarea al llegar a la etapa por un verbo, una serie o una plantilla. Dos guardados:
/// el índice único no admite dos por defecto ni un instante.
/// </summary>
public static class SetDefaultWorkflowStatusHandler
{
    public static async Task<Result> Handle(
        SetDefaultWorkflowStatusCommand command,
        IWorkflowStatusRepository statuses,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var target = await WorkflowStatusCatalog.ResolveAsync(
            statuses,
            command.TenantId,
            command.Code,
            command.ByUserId,
            DateTime.UtcNow,
            ct
        );
        if (target.IsFailure)
            return Result.Failure(target.Error);

        foreach (
            var row in (await statuses.ListAsync(command.TenantId, false, ct)).Where(r =>
                r.Category == target.Value.Category
            )
        )
            row.ClearDefault();
        await unitOfWork.SaveChangesAsync(ct);

        if (target.Value.Id is { } id)
        {
            var chosen = await statuses.GetByIdAsync(command.TenantId, id, ct);
            var made = chosen.Value.MakeDefault();
            if (made.IsFailure)
                return made;
            await unitOfWork.SaveChangesAsync(ct);
        }
        return Result.Success();
    }
}
