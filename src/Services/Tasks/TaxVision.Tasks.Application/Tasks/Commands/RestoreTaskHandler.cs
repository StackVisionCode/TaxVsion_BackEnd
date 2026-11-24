using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Tasks.Commands;

public sealed record RestoreTaskCommand(Guid TenantId, Guid TaskId, Guid ByUserId, bool HasManageAll);

/// <summary>
/// Saca de la papelera la tarea que se borró y todo lo que se fue con ella (D-25). Vuelve en el estado que tenía; sus
/// dependencias no vuelven. La madre tiene que estar viva y una ocurrencia abierta que la serie ya reemplazó no vuelve.
/// </summary>
public static class RestoreTaskHandler
{
    public static async Task<Result> Handle(
        RestoreTaskCommand command,
        ITaskRepository tasks,
        ITaskSeriesRepository series,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await tasks.GetIncludingTrashAsync(command.TenantId, command.TaskId, ct);
        if (found.IsFailure)
            return Result.Failure(found.Error);
        var root = found.Value;
        if (root.TrashRootId != root.Id)
            return Result.Failure(TaskErrors.Trash.NotInTrash);
        if (!TaskAccessPolicy.CanMutate(root, command.ByUserId, command.HasManageAll))
            return Result.Failure(TaskErrors.Forbidden);

        var allowed = await CheckParentAndSeriesAsync(root, tasks, series, ct);
        if (allowed.IsFailure)
            return allowed;

        var now = DateTime.UtcNow;
        foreach (var task in await tasks.ListTrashBranchAsync(command.TenantId, root.Id, ct))
            task.Restore(command.ByUserId, now);
        await RelinkAsync(root, tasks, series, ct);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static bool IsOpen(TaskItem task) => TaskItemStatuses.IsOpen(task.Status);

    private static async Task<Result> CheckParentAndSeriesAsync(
        TaskItem root,
        ITaskRepository tasks,
        ITaskSeriesRepository series,
        CancellationToken ct
    )
    {
        if (root.ParentTaskId is { } parentId && (await tasks.GetByIdAsync(root.TenantId, parentId, ct)).IsFailure)
            return Result.Failure(TaskErrors.Trash.ParentInTrash);
        if (!IsOpen(root) || root.SeriesId is not { } seriesId || root.OccurrenceNumber is not { } number)
            return Result.Success();
        var found = await series.GetByIdAsync(root.TenantId, seriesId, ct);
        return found.IsSuccess ? found.Value.CanReopenOccurrence(root.Id, number) : Result.Success();
    }

    /// <summary>La madre vuelve a contar la rama; la última ocurrencia vuelve a ser la abierta de su serie.</summary>
    private static async Task RelinkAsync(
        TaskItem root,
        ITaskRepository tasks,
        ITaskSeriesRepository series,
        CancellationToken ct
    )
    {
        if (root.ParentTaskId is { } parentId)
        {
            var parent = await tasks.GetByIdAsync(root.TenantId, parentId, ct);
            if (parent.IsSuccess)
                parent.Value.RegisterSubtaskRestored(IsOpen(root));
        }
        if (IsOpen(root) && root.SeriesId is { } seriesId && root.OccurrenceNumber is { } number)
        {
            var found = await series.GetByIdAsync(root.TenantId, seriesId, ct);
            if (found.IsSuccess)
                found.Value.RegisterInstanceReopened(root.Id, number);
        }
    }
}
