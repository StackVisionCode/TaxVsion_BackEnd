using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Tasks.Abstractions;

/// <summary>Lector del tablero por columnas: totales agrupados y tarjetas como proyección ligera.</summary>
public interface ITaskBoardReader
{
    /// <summary>Tareas por etapa y código guardado, con los filtros y la visibilidad del tablero.</summary>
    Task<IReadOnlyList<TaskBoardTally>> CountAsync(
        Guid tenantId,
        TaskQueryFilter filter,
        Guid? assignedToUserId,
        CancellationToken ct = default
    );

    Task<IReadOnlyList<TaskCardResponse>> ListCardsAsync(
        Guid tenantId,
        TaskQueryFilter filter,
        TaskBoardSort sort,
        int skip,
        int take,
        Guid? assignedToUserId,
        CancellationToken ct = default
    );
}

public sealed record TaskBoardTally(TaskItemStatus Category, string? Code, int Count);

/// <summary>Orden de las tarjetas. Completed siempre va por la más reciente.</summary>
public enum TaskBoardSort
{
    Due,
    Priority,
    RecentlyCompleted,
}
