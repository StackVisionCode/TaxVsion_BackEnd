using TaxVision.Customer.Domain.Imports;

namespace TaxVision.Customer.Application.Abstractions;

public interface ICustomerImportRepository
{
    Task<CustomerImportAttempt?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Idempotency check: si existe un attempt con la misma key para el tenant, lo devuelve.</summary>
    Task<CustomerImportAttempt?> GetByIdempotencyKeyAsync(Guid tenantId, string idempotencyKey, CancellationToken ct);

    /// <summary>Cuenta cuantos jobs activos (no terminales) tiene el tenant para enforce 1-active-per-tenant.</summary>
    Task<int> CountActiveByTenantAsync(Guid tenantId, CancellationToken ct);

    Task AddAsync(CustomerImportAttempt attempt, CancellationToken ct);
}
