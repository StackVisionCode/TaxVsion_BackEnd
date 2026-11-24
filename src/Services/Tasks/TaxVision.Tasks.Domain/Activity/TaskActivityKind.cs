namespace TaxVision.Tasks.Domain.Activity;

/// <summary>Qué pasó en la tarea. Se guarda como texto: el historial se lee años después.</summary>
public enum TaskActivityKind
{
    Created,
    SubtaskAdded,
    Started,
    MovedToNotStarted,
    StatusChanged,
    WaitingOnClient,
    Completed,
    Cancelled,
    Reopened,
    Assigned,
    Unassigned,
    DueChanged,
    PriorityChanged,
    DependencyAdded,
    DependencyRemoved,
    TimeEntryEdited,
    TimeEntryRemoved,
    MovedToTrash,
    Restored,
    ReferenceChanged,
}
