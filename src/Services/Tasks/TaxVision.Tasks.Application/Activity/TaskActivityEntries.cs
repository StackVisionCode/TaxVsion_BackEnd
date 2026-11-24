using System.Globalization;
using BuildingBlocks.TimeZones;
using TaxVision.Tasks.Domain.Activity;
using TaxVision.Tasks.Domain.Tasks.Events;

namespace TaxVision.Tasks.Application.Activity;

/// <summary>Una entrada del historial por armar: de qué tarea, qué pasó, quién, cuándo y con qué detalles.</summary>
public sealed record TaskActivityEntry(
    Guid TenantId,
    Guid TaskId,
    TaskActivityKind Kind,
    Guid? ActorUserId,
    DateTime OccurredAtUtc,
    Dictionary<string, string?>? Data = null
)
{
    /// <summary>Los detalles vacíos no se guardan.</summary>
    public TaskActivity ToActivity() =>
        TaskActivity.Record(
            TenantId,
            TaskId,
            Kind,
            ActorUserId,
            OccurredAtUtc,
            TaskActivityData.Write(Data?.Where(p => p.Value is not null).ToDictionary())
        );
}

/// <summary>Cada evento de la tarea como entrada del historial. Los días van en días de la oficina.</summary>
public static class TaskActivityEntries
{
    public static TaskActivityEntry Of(TaskCreatedDomainEvent e) =>
        new(
            e.TenantId,
            e.TaskId,
            TaskActivityKind.Created,
            e.CreatedByUserId,
            e.CreatedAtUtc,
            new() { ["recurring"] = e.RecurringOccurrence ? "true" : null }
        );

    /// <summary>La subtarea nueva, en el historial de su madre.</summary>
    public static TaskActivityEntry SubtaskAdded(TaskCreatedDomainEvent e, Guid parentId, string? title) =>
        new(
            e.TenantId,
            parentId,
            TaskActivityKind.SubtaskAdded,
            e.CreatedByUserId,
            e.CreatedAtUtc,
            new() { ["subtaskId"] = e.TaskId.ToString(), ["title"] = title }
        );

    public static TaskActivityEntry Of(TaskDeletedDomainEvent e) =>
        new(e.TenantId, e.TaskId, TaskActivityKind.MovedToTrash, e.DeletedByUserId, e.DeletedAtUtc);

    public static TaskActivityEntry Of(TaskRestoredDomainEvent e) =>
        new(e.TenantId, e.TaskId, TaskActivityKind.Restored, e.RestoredByUserId, e.RestoredAtUtc);

    public static TaskActivityEntry Of(TaskStartedDomainEvent e) =>
        new(e.TenantId, e.TaskId, TaskActivityKind.Started, e.StartedByUserId, e.StartedAtUtc);

    public static TaskActivityEntry Of(TaskMovedToNotStartedDomainEvent e) =>
        new(
            e.TenantId,
            e.TaskId,
            TaskActivityKind.MovedToNotStarted,
            e.MovedByUserId,
            e.MovedAtUtc,
            new() { ["fromCategory"] = e.PreviousStatus.ToString() }
        );

    public static TaskActivityEntry Of(TaskWorkflowStatusChangedDomainEvent e) =>
        new(
            e.TenantId,
            e.TaskId,
            TaskActivityKind.StatusChanged,
            e.ByUserId,
            e.AtUtc,
            new()
            {
                ["fromStatusId"] = e.FromStatusId?.ToString(),
                ["toStatusId"] = e.ToStatusId?.ToString(),
                ["fromCategory"] = e.FromCategory.ToString(),
                ["toCategory"] = e.ToCategory.ToString(),
            }
        );

    public static TaskActivityEntry Of(TaskMovedToWaitingOnClientDomainEvent e) =>
        new(
            e.TenantId,
            e.TaskId,
            TaskActivityKind.WaitingOnClient,
            e.RequestedByUserId,
            e.RequestedAtUtc,
            new() { ["clientDueAtUtc"] = e.ClientDueAtUtc?.ToString("O", CultureInfo.InvariantCulture) }
        );

    public static TaskActivityEntry Of(TaskCompletedDomainEvent e) =>
        new(e.TenantId, e.TaskId, TaskActivityKind.Completed, e.CompletedByUserId, e.CompletedAtUtc);

    public static TaskActivityEntry Of(TaskCancelledDomainEvent e) =>
        new(
            e.TenantId,
            e.TaskId,
            TaskActivityKind.Cancelled,
            e.CancelledByUserId,
            e.CancelledAtUtc,
            new() { ["reason"] = e.Reason }
        );

    public static TaskActivityEntry Of(TaskReopenedDomainEvent e) =>
        new(
            e.TenantId,
            e.TaskId,
            TaskActivityKind.Reopened,
            e.ReopenedByUserId,
            e.ReopenedAtUtc,
            new() { ["toCategory"] = e.ResumedStatus.ToString() }
        );

    public static TaskActivityEntry Of(TaskAssignedDomainEvent e) =>
        new(
            e.TenantId,
            e.TaskId,
            TaskActivityKind.Assigned,
            e.AssignedByUserId,
            e.AssignedAtUtc,
            new()
            {
                ["assigneeUserId"] = e.AssigneeUserId.ToString(),
                ["previousAssigneeUserId"] = e.PreviousAssigneeUserId?.ToString(),
            }
        );

    public static TaskActivityEntry Of(TaskUnassignedDomainEvent e) =>
        new(
            e.TenantId,
            e.TaskId,
            TaskActivityKind.Unassigned,
            e.UnassignedByUserId,
            e.UnassignedAtUtc,
            new() { ["previousAssigneeUserId"] = e.PreviousAssigneeUserId.ToString() }
        );

    public static TaskActivityEntry Of(TaskDueChangedDomainEvent e) =>
        new(
            e.TenantId,
            e.TaskId,
            TaskActivityKind.DueChanged,
            e.ChangedByUserId,
            e.ChangedAtUtc,
            new()
            {
                ["previousDueOn"] = CivilOf(e.PreviousDueAtUtc, e.TimeZoneId),
                ["dueOn"] = CivilOf(e.NewDueAtUtc, e.TimeZoneId),
                ["reason"] = e.StatutoryChangeReason,
            }
        );

    public static TaskActivityEntry Of(TaskReferenceChangedDomainEvent e) =>
        new(
            e.TenantId,
            e.TaskId,
            TaskActivityKind.ReferenceChanged,
            e.ChangedByUserId,
            e.ChangedAtUtc,
            new()
            {
                ["previousCustomerId"] = e.PreviousCustomerId?.ToString(),
                ["customerId"] = e.CustomerId?.ToString(),
                ["previousTaxYear"] = e.PreviousTaxYear?.ToString(CultureInfo.InvariantCulture),
                ["taxYear"] = e.TaxYear?.ToString(CultureInfo.InvariantCulture),
            }
        );

    public static TaskActivityEntry Of(TaskPriorityChangedDomainEvent e) =>
        new(
            e.TenantId,
            e.TaskId,
            TaskActivityKind.PriorityChanged,
            e.ChangedByUserId,
            e.ChangedAtUtc,
            new() { ["from"] = e.PreviousPriority.ToString(), ["to"] = e.NewPriority.ToString() }
        );

    public static TaskActivityEntry Of(TaskDependencyChangedDomainEvent e, string? otherTitle) =>
        new(
            e.TenantId,
            e.TaskId,
            e.Added ? TaskActivityKind.DependencyAdded : TaskActivityKind.DependencyRemoved,
            e.ByUserId,
            e.AtUtc,
            new() { ["dependsOnTaskId"] = e.DependsOnTaskId.ToString(), ["title"] = otherTitle }
        );

    public static TaskActivityEntry Of(TaskTimeEntryChangedDomainEvent e) =>
        new(
            e.TenantId,
            e.TaskId,
            e.Hours is null ? TaskActivityKind.TimeEntryRemoved : TaskActivityKind.TimeEntryEdited,
            e.ByUserId,
            e.AtUtc,
            new()
            {
                ["entryUserId"] = e.EntryUserId.ToString(),
                ["previousHours"] = Hours(e.PreviousHours),
                ["hours"] = e.Hours is { } hours ? Hours(hours) : null,
            }
        );

    /// <summary>El día del vencimiento en la zona de la tarea; los datos viejos de medianoche UTC se leen tal cual.</summary>
    private static string? CivilOf(DateTime? dueAtUtc, string? timeZoneId)
    {
        if (dueAtUtc is not { } due)
            return null;
        var day =
            due.TimeOfDay == TimeSpan.Zero
                ? DateOnly.FromDateTime(due)
                : CivilDate.Of(due, ResolvedTimeZone.Resolve(timeZoneId));
        return day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string Hours(decimal hours) => hours.ToString("0.##", CultureInfo.InvariantCulture);
}
