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

    Task<IReadOnlyList<TaskSeries>> ListAsync(Guid tenantId, SeriesStatus? status, CancellationToken ct = default);

    /// <summary>
    /// Las activas sin instancia abierta, de todos los tenants: es el barrido de fondo, no una
    /// consulta de usuario. Un tenant en el predicado lo dejaría sin poder recuperar a los demás.
    /// </summary>
    Task<IReadOnlyList<TaskSeries>> ListPendingMaterializationAsync(int take, CancellationToken ct = default);
}
