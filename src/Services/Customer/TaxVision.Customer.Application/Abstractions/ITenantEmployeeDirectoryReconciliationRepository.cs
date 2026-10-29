using TaxVision.Customer.Domain.Employees;

namespace TaxVision.Customer.Application.Abstractions;

/// <summary>Lectura por lote y alta del directorio de empleados, solo para la reconciliación con Auth.</summary>
public interface ITenantEmployeeDirectoryReconciliationRepository
{
    Task<IReadOnlyDictionary<Guid, TenantEmployeeDirectoryEntry>> GetByUserIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken ct = default
    );

    void Add(TenantEmployeeDirectoryEntry entry);
}
