using BuildingBlocks.Persistence;
using TaxVision.Tasks.Application.Activity.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks.Events;

namespace TaxVision.Tasks.Application.Activity;

/// <summary>
/// Escribe el historial desde los eventos de dominio de la tarea (cola durable, después de confirmar el cambio). Los
/// nombres de personas y estados se resuelven al leer.
/// </summary>
public static class TaskActivityHandler
{
    public static async Task Handle(
        TaskCreatedDomainEvent e,
        ITaskActivityRepository activities,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        activities.Add(TaskActivityEntries.Of(e).ToActivity());
        if (e.ParentTaskId is { } parentId)
        {
            var child = await tasks.GetByIdAsync(e.TenantId, e.TaskId, ct);
            var title = child.IsSuccess ? child.Value.Title.Value : null;
            activities.Add(TaskActivityEntries.SubtaskAdded(e, parentId, title).ToActivity());
        }
        await unitOfWork.SaveChangesAsync(ct);
    }

    public static async Task Handle(
        TaskDependencyChangedDomainEvent e,
        ITaskActivityRepository activities,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var other = await tasks.GetByIdAsync(e.TenantId, e.DependsOnTaskId, ct);
        var title = other.IsSuccess ? other.Value.Title.Value : null;
        await SaveAsync(activities, unitOfWork, TaskActivityEntries.Of(e, title), ct);
    }

    public static Task Handle(
        TaskDeletedDomainEvent e,
        ITaskActivityRepository activities,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        return SaveAsync(activities, unitOfWork, TaskActivityEntries.Of(e), ct);
    }

    public static Task Handle(
        TaskRestoredDomainEvent e,
        ITaskActivityRepository activities,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        return SaveAsync(activities, unitOfWork, TaskActivityEntries.Of(e), ct);
    }

    public static Task Handle(
        TaskStartedDomainEvent e,
        ITaskActivityRepository activities,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        return SaveAsync(activities, unitOfWork, TaskActivityEntries.Of(e), ct);
    }

    public static Task Handle(
        TaskMovedToNotStartedDomainEvent e,
        ITaskActivityRepository activities,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        return SaveAsync(activities, unitOfWork, TaskActivityEntries.Of(e), ct);
    }

    public static Task Handle(
        TaskWorkflowStatusChangedDomainEvent e,
        ITaskActivityRepository activities,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        return SaveAsync(activities, unitOfWork, TaskActivityEntries.Of(e), ct);
    }

    public static Task Handle(
        TaskMovedToWaitingOnClientDomainEvent e,
        ITaskActivityRepository activities,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        return SaveAsync(activities, unitOfWork, TaskActivityEntries.Of(e), ct);
    }

    public static Task Handle(
        TaskCompletedDomainEvent e,
        ITaskActivityRepository activities,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        return SaveAsync(activities, unitOfWork, TaskActivityEntries.Of(e), ct);
    }

    public static Task Handle(
        TaskCancelledDomainEvent e,
        ITaskActivityRepository activities,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        return SaveAsync(activities, unitOfWork, TaskActivityEntries.Of(e), ct);
    }

    public static Task Handle(
        TaskReopenedDomainEvent e,
        ITaskActivityRepository activities,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        return SaveAsync(activities, unitOfWork, TaskActivityEntries.Of(e), ct);
    }

    public static Task Handle(
        TaskAssignedDomainEvent e,
        ITaskActivityRepository activities,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        return SaveAsync(activities, unitOfWork, TaskActivityEntries.Of(e), ct);
    }

    public static Task Handle(
        TaskUnassignedDomainEvent e,
        ITaskActivityRepository activities,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        return SaveAsync(activities, unitOfWork, TaskActivityEntries.Of(e), ct);
    }

    public static Task Handle(
        TaskDueChangedDomainEvent e,
        ITaskActivityRepository activities,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        return SaveAsync(activities, unitOfWork, TaskActivityEntries.Of(e), ct);
    }

    public static Task Handle(
        TaskReferenceChangedDomainEvent e,
        ITaskActivityRepository activities,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        return SaveAsync(activities, unitOfWork, TaskActivityEntries.Of(e), ct);
    }

    public static Task Handle(
        TaskPriorityChangedDomainEvent e,
        ITaskActivityRepository activities,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        return SaveAsync(activities, unitOfWork, TaskActivityEntries.Of(e), ct);
    }

    public static Task Handle(
        TaskTimeEntryChangedDomainEvent e,
        ITaskActivityRepository activities,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        return SaveAsync(activities, unitOfWork, TaskActivityEntries.Of(e), ct);
    }

    private static async Task SaveAsync(
        ITaskActivityRepository activities,
        IUnitOfWork unitOfWork,
        TaskActivityEntry entry,
        CancellationToken ct
    )
    {
        activities.Add(entry.ToActivity());
        await unitOfWork.SaveChangesAsync(ct);
    }
}
