using BuildingBlocks.Results;
using TaxVision.Tasks.Domain.Labels;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Application.Labels.Abstractions;

/// <summary>
/// Lecturas con <c>IgnoreQueryFilters()</c> y el tenant en el predicado, igual que el resto: los
/// handlers corren en el scope de Wolverine, sin <c>TenantContext</c>.
/// </summary>
public interface ITaskLabelRepository
{
    void Add(TaskLabel label);

    void Remove(TaskLabel label);

    Task<Result<TaskLabel>> GetByIdAsync(Guid tenantId, Guid labelId, CancellationToken ct = default);

    Task<bool> CodeExistsAsync(
        Guid tenantId,
        TaskLabelCode code,
        Guid? excludingLabelId,
        CancellationToken ct = default
    );

    /// <summary>Ordenado por <c>SortOrder</c>: es el orden en que la firma quiere verlos.</summary>
    Task<IReadOnlyList<TaskLabel>> ListAsync(Guid tenantId, CancellationToken ct = default);
}
