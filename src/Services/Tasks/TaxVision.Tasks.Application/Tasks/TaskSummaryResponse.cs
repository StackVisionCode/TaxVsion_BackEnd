using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Tasks;

/// <summary>
/// Resumen de tareas del dashboard (widgets Tasks y Filing deadlines, y el saludo). Abiertas = sin completar ni
/// cancelar. <see cref="OverdueAssigneeUserId"/> solo trae valor si todas las vencidas son de la misma persona.
/// </summary>
public sealed record TaskSummaryResponse(
    DateOnly Today,
    int Open,
    TaskStatusCounts ByStatus,
    int Overdue,
    int DueToday,
    int DueSoon,
    int StatutoryOverdue,
    int CompletedToday,
    Guid? OverdueAssigneeUserId,
    IReadOnlyList<TaskSummaryItem> Top,
    IReadOnlyList<TaskDeadlineGroup> Deadlines,
    bool DeadlinesTruncated
);

public sealed record TaskStatusCounts(int NotStarted, int InProgress, int WaitingOnClient);

public sealed record TaskSummaryItem(
    Guid Id,
    string Title,
    TaskItemStatus Status,
    DateTime? DueAtUtc,
    DateOnly? DueOn,
    bool DueIsStatutory,
    Guid? AssigneeUserId,
    Guid? CustomerId
);

/// <summary>Vencimientos legales abiertos con la misma fecha y el mismo título; sin cliente cuenta la tarea.</summary>
public sealed record TaskDeadlineGroup(DateOnly DueOn, string Title, int ClientCount, int TaskCount);
