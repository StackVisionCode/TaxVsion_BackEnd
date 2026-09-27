using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Series.Commands;

public sealed record EndTaskSeriesCommand(
    Guid TenantId,
    Guid SeriesId,
    Guid ByUserId = default,
    bool HasManageAll = false
);

/// <summary>
/// Terminar la serie no borra la instancia abierta: la tarea de este trimestre se sigue trabajando,
/// lo que no habrá es la del siguiente.
/// </summary>
public static class EndTaskSeriesHandler
{
    public static async Task<Result<TaskSeriesResponse>> Handle(
        EndTaskSeriesCommand command,
        ITaskSeriesRepository seriesRepository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await seriesRepository.GetByIdAsync(command.TenantId, command.SeriesId, ct);
        if (found.IsFailure)
            return Result.Failure<TaskSeriesResponse>(found.Error);

        // A1 — terminar la serie de otro le hace desaparecer las tareas siguientes sin explicación. Antes
        // alcanzaba con tasks.write.
        if (!TaskSeriesAccessPolicy.CanMutate(found.Value, command.ByUserId, command.HasManageAll))
            return Result.Failure<TaskSeriesResponse>(TaskErrors.Forbidden);

        found.Value.End();
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(TaskSeriesResponse.From(found.Value));
    }
}
