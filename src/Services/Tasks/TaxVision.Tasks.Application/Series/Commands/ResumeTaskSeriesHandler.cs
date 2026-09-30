using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Series.Commands;

public sealed record ResumeTaskSeriesCommand(
    Guid TenantId,
    Guid SeriesId,
    Guid ByUserId = default,
    bool HasManageAll = false
);

/// <summary>
/// Reanudar siembra desde ahora. Si no quedó instancia abierta se materializa una acá mismo, para que
/// la serie no dependa del barrido para volver a la vida.
/// </summary>
public static class ResumeTaskSeriesHandler
{
    public static async Task<Result<TaskSeriesResponse>> Handle(
        ResumeTaskSeriesCommand command,
        ITaskSeriesRepository seriesRepository,
        ITaskSeriesMaterializer materializer,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await seriesRepository.GetByIdAsync(command.TenantId, command.SeriesId, ct);
        if (found.IsFailure)
            return Result.Failure<TaskSeriesResponse>(found.Error);

        // A1 — reanudar la serie de otro le hace desaparecer las tareas siguientes sin explicación. Antes
        // alcanzaba con tasks.write.
        if (!TaskSeriesAccessPolicy.CanMutate(found.Value, command.ByUserId, command.HasManageAll))
            return Result.Failure<TaskSeriesResponse>(TaskErrors.Forbidden);

        var series = found.Value;
        var resumed = series.Resume(DateTime.UtcNow);
        if (resumed.IsFailure)
            return Result.Failure<TaskSeriesResponse>(resumed.Error);

        if (series.OpenInstanceId is null)
            await materializer.MaterializeNextAsync(series, null, null, ct);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(TaskSeriesResponse.From(series));
    }
}
