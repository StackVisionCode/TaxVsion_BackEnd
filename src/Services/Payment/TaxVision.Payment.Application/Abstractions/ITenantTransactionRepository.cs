using TaxVision.Payment.Domain.TenantPayments;

namespace TaxVision.Payment.Application.Abstractions;

public interface ITenantTransactionRepository
{
    Task<TenantTransaction?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<TenantTransaction>> GetByTenantAsync(Guid tenantId, int page, int size, CancellationToken ct = default);
    Task AddAsync(TenantTransaction transaction, CancellationToken ct = default);
}
