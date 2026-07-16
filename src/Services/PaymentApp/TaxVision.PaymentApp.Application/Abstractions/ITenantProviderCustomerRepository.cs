using TaxVision.PaymentApp.Domain.ProviderCustomers;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.Abstractions;

public interface ITenantProviderCustomerRepository
{
    Task<TenantProviderCustomer?> GetByTenantAndProviderAsync(Guid tenantId, PaymentProviderCode code, CancellationToken ct = default);
    Task<TenantProviderCustomer?> GetByIdAsync(Guid tenantProviderCustomerId, Guid tenantId, CancellationToken ct = default);

    /// <summary>Métodos guardados que vencen antes de <paramref name="cutoffUtc"/> —
    /// batch job query, cross-tenant por diseño, solo la llama
    /// <c>ExpiringPaymentMethodsJob</c>.</summary>
    Task<IReadOnlyList<TenantProviderCustomer>> GetWithMethodsExpiringBeforeAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct = default);

    Task AddAsync(TenantProviderCustomer customer, CancellationToken ct = default);
}
