namespace TaxVision.Tasks.Domain.Tasks;

/// <summary>La única definición de "abierta" y "cerrada". Las consultas usan <see cref="Open"/>; el código, los métodos.</summary>
public static class TaskItemStatuses
{
    /// <summary>Lo que no llegó a un terminal.</summary>
    public static readonly TaskItemStatus[] Open =
    [
        TaskItemStatus.NotStarted,
        TaskItemStatus.InProgress,
        TaskItemStatus.WaitingOnClient,
    ];

    public static bool IsClosed(TaskItemStatus status) =>
        status is TaskItemStatus.Completed or TaskItemStatus.Cancelled;

    public static bool IsOpen(TaskItemStatus status) => !IsClosed(status);
}
