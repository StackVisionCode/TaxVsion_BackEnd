using TaxVision.PaymentApp.Domain.Billing;

namespace TaxVision.PaymentApp.Application.Billing.Abstractions;

public interface ITenantBillingPreferenceProjectionRepository
{
    Task<TenantBillingPreferenceProjection?> GetAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(TenantBillingPreferenceProjection projection, CancellationToken ct = default);
}
