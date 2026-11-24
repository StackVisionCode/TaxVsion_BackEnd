using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Tasks.Abstractions;

/// <summary>
/// Los filtros que comparten la búsqueda y el tablero. Van juntos en un record y no como parámetros
/// sueltos porque siempre viajan los cinco y el orden posicional se equivoca solo.
/// </summary>
/// <param name="Text">Coincidencia parcial sobre el título; vacío significa «sin filtro».</param>
/// <param name="OnlyOpen">Excluye completadas y canceladas.</param>
/// <param name="Due">Solo abiertas con ese vencimiento, con la regla del resumen del dashboard.</param>
/// <param name="RootOnly">Solo tareas principales: las subtareas no son tarjetas del tablero.</param>
/// <param name="AlsoSubtasksOf">Con <c>RootOnly</c>: también las subtareas asignadas a esta persona (su lista, D-8).</param>
public sealed record TaskQueryFilter(
    string? Text = null,
    TaskItemStatus? Status = null,
    Guid? AssigneeUserId = null,
    Guid? CustomerId = null,
    int? TaxYear = null,
    bool OnlyOpen = false,
    TaskDueFilter? Due = null,
    TaskStatusCodeFilter? StatusCode = null,
    TaskPriority? Priority = null,
    bool RootOnly = false,
    Guid? AlsoSubtasksOf = null
);

/// <summary>
/// Un estado de la oficina. Si es el por defecto de su etapa, también cuentan las tareas de esa etapa sin
/// estado propio (las que llegaron por un verbo, una serie o una plantilla).
/// </summary>
/// <param name="ExcludedCodes">
/// Solo con el por defecto: los otros estados activos de la etapa. Lo que no esté en ellos (sin estado propio o con
/// uno archivado) cae en el por defecto. Null conserva la regla de la búsqueda.
/// </param>
public sealed record TaskStatusCodeFilter(
    string Code,
    TaskItemStatus Category,
    bool IsCategoryDefault,
    IReadOnlyList<string>? ExcludedCodes = null
);
