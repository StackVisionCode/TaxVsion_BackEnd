using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Tasks;

/// <summary>Tarjeta del tablero: lo que se pinta, sin el agregado completo.</summary>
public sealed record TaskCardResponse(
    Guid Id,
    string Title,
    TaskItemStatus Status,
    string? WorkflowStatusCode,
    TaskPriority Priority,
    Guid? AssigneeUserId,
    Guid? CustomerId,
    int? TaxYear,
    DateTime? DueAtUtc,
    DateOnly? DueOn,
    string? DueTimeZoneId,
    bool DueIsStatutory,
    bool IsBlocked,
    int OpenBlockerCount,
    int OpenSubtaskCount,
    int SubtaskCount,
    decimal? EstimatedHours,
    string? ExpectedItems,
    DateTime? CompletedAtUtc,
    bool IsRecurring
);

/// <summary>
/// Una columna: un estado activo, su total real y, si se pidió, sus primeras tarjetas. <c>NextCursor</c> abre
/// "Load more"; null cuando no hay más.
/// </summary>
public sealed record TaskBoardColumnSlice(
    string Code,
    TaskItemStatus Category,
    int Total,
    IReadOnlyList<TaskCardResponse>? Items,
    string? NextCursor
);

public sealed record TaskBoardColumnsResponse(IReadOnlyList<TaskBoardColumnSlice> Columns);
