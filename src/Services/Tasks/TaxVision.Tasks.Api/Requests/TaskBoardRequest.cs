using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Api.Requests;

/// <summary>Los filtros del tablero, los mismos de la búsqueda más prioridad y orden.</summary>
public sealed record TaskBoardRequest
{
    public string? Q { get; init; }
    public Guid? AssigneeUserId { get; init; }
    public Guid? CustomerId { get; init; }
    public int? TaxYear { get; init; }
    public TaskPriority? Priority { get; init; }
    public TaskDueBucket? Due { get; init; }

    /// <summary>Códigos separados por coma; sin él, todas las columnas traen tarjetas.</summary>
    public string? Expanded { get; init; }
    public int? Take { get; init; }
    public TaskBoardSort? Sort { get; init; }

    /// <summary>Solo tareas principales (D-8). Por defecto, sí.</summary>
    public bool RootOnly { get; init; } = true;

    public TaskQueryFilter ToFilter() =>
        new(Q, null, AssigneeUserId, CustomerId, TaxYear, Priority: Priority, RootOnly: RootOnly);
}
