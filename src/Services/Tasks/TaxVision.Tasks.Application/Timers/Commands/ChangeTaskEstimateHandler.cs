using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Application.Timers.Commands;

/// <param name="EstimatedMinutes">Null quita la estimación.</param>
public sealed record ChangeTaskEstimateCommand(
    Guid TenantId,
    Guid TaskId,
    Guid ByUserId,
    bool HasManageAll,
    int? EstimatedMinutes
);

/// <summary>Cambia o quita la estimación, con la misma política de propiedad que editar la tarea.</summary>
public static class ChangeTaskEstimateHandler
{
    public static async Task<Result<TaskResponse>> Handle(
        ChangeTaskEstimateCommand command,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var estimate = ToEstimate(command.EstimatedMinutes);
        if (estimate.IsFailure)
            return Result.Failure<TaskResponse>(estimate.Error);

        var found = await tasks.GetByIdAsync(command.TenantId, command.TaskId, ct);
        if (found.IsFailure)
            return Result.Failure<TaskResponse>(found.Error);
        if (!TaskAccessPolicy.CanMutate(found.Value, command.ByUserId, command.HasManageAll))
            return Result.Failure<TaskResponse>(TaskErrors.Forbidden);

        found.Value.ChangeEstimate(estimate.Value);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(TaskResponse.From(found.Value));
    }

    // Minutos enteros en la API; horas con dos decimales en el dominio.
    private static Result<EstimatedHours?> ToEstimate(int? minutes)
    {
        if (minutes is null)
            return Result.Success<EstimatedHours?>(null);
        var created = EstimatedHours.Create(Math.Round(minutes.Value / 60m, EstimatedHours.Scale));
        return created.IsFailure
            ? Result.Failure<EstimatedHours?>(created.Error)
            : Result.Success<EstimatedHours?>(created.Value);
    }
}
