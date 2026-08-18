using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Tasks.Abstractions;

/// <summary>
/// Los filtros que comparten la búsqueda y el tablero. Van juntos en un record y no como parámetros
/// sueltos porque siempre viajan los cinco y el orden posicional se equivoca solo.
/// </summary>
/// <param name="Text">Coincidencia parcial sobre el título; vacío significa «sin filtro».</param>
/// <param name="OnlyOpen">Excluye completadas y canceladas.</param>
public sealed record TaskQueryFilter(
    string? Text = null,
    TaskItemStatus? Status = null,
    Guid? AssigneeUserId = null,
    Guid? CustomerId = null,
    int? TaxYear = null,
    bool OnlyOpen = false
);
