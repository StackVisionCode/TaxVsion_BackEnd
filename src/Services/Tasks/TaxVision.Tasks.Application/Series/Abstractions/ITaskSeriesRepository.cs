using BuildingBlocks.Results;
using TaxVision.Tasks.Domain.Series;

namespace TaxVision.Tasks.Application.Series.Abstractions;

/// <summary>
/// Lecturas con <c>IgnoreQueryFilters()</c> y el tenant en el predicado, igual que el resto: los
/// handlers corren en el scope de Wolverine, sin <c>TenantContext</c>.
/// </summary>
public interface ITaskSeriesRepository
{
    void Add(TaskSeries series);

    Task<Result<TaskSeries>> GetByIdAsync(Guid tenantId, Guid seriesId, CancellationToken ct = default);

    /// <summary>Guardar solo si la serie sigue en la versión que vio quien la edita; si no, conflicto.</summary>
    void ExpectVersion(TaskSeries series, byte[] rowVersion);

    Task<IReadOnlyList<TaskSeries>> ListAsync(Guid tenantId, SeriesStatus? status, CancellationToken ct = default);

    /// <summary>Una página, la más reciente primero, y el total del filtro.</summary>
    Task<(IReadOnlyList<TaskSeries> Items, int Total)> ListPageAsync(
        SeriesListFilter filter,
        CancellationToken ct = default
    );

    /// <summary>
    /// Las activas sin instancia abierta, de todos los tenants: es el barrido de fondo, no una
    /// consulta de usuario. Un tenant en el predicado lo dejaría sin poder recuperar a los demás.
    /// </summary>
    Task<IReadOnlyList<TaskSeries>> ListPendingMaterializationAsync(
        int take,
        DateTime nowUtc,
        CancellationToken ct = default
    );
}

/// <param name="OnlyFor">"Mías": las que creó o le tocan a este usuario.</param>
/// <param name="VisibleTo">Sin ver todos los clientes: las propias, las sin cliente y las de sus clientes.</param>
public sealed record SeriesListFilter(
    Guid TenantId,
    SeriesStatus? Status,
    Guid? OnlyFor,
    Guid? VisibleTo,
    int Page,
    int Size
);
