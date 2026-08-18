using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Series.Abstractions;

namespace TaxVision.Tasks.Application.Series.Commands;

public sealed record PauseTaskSeriesCommand(Guid TenantId, Guid SeriesId);

/// <summary>
/// Pausar no toca la instancia abierta: la tarea que ya está en la lista de alguien sigue ahí. Lo que
/// se detiene es la siembra de la siguiente.
/// </summary>
public static class PauseTaskSeriesHandler
{
    public static async Task<Result<TaskSeriesResponse>> Handle(
        PauseTaskSeriesCommand command,
        ITaskSeriesRepository seriesRepository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await seriesRepository.GetByIdAsync(command.TenantId, command.SeriesId, ct);
        if (found.IsFailure)
            return Result.Failure<TaskSeriesResponse>(found.Error);

        var paused = found.Value.Pause();
        if (paused.IsFailure)
            return Result.Failure<TaskSeriesResponse>(paused.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(TaskSeriesResponse.From(found.Value));
    }
}
