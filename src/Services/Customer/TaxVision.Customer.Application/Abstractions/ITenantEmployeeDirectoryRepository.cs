using TaxVision.Customer.Domain.Employees;

namespace TaxVision.Customer.Application.Abstractions;

/// <summary>
/// Proyeccion local de usuarios del staff del tenant, alimentada por eventos de Auth.
/// Usada por <c>AssignPreparerHandler</c> para validar que un <c>PreparerUserId</c>
/// es un empleado activo real antes de asignarlo a un Customer.
/// </summary>
public interface ITenantEmployeeDirectoryRepository
{
    Task<TenantEmployeeDirectoryEntry?> GetByUserIdAsync(Guid userId, CancellationToken ct = default);

    Task UpsertAsync(Guid userId, Guid tenantId, string actorType, bool isActive, CancellationToken ct = default);

    Task MarkActiveAsync(Guid userId, CancellationToken ct = default);

    Task MarkInactiveAsync(Guid userId, CancellationToken ct = default);
}
