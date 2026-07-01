using TaxVision.Payment.Domain.TenantPayments;

namespace TaxVision.Payment.Application.Abstractions;

public interface ITenantPaymentConfigRepository
{
    Task<TenantPaymentConfig?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(TenantPaymentConfig config, CancellationToken ct = default);
}
