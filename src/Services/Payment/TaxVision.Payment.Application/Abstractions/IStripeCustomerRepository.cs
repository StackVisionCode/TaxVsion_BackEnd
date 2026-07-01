using TaxVision.Payment.Domain.StripeCustomers;

namespace TaxVision.Payment.Application.Abstractions;

public interface IStripeCustomerRepository
{
    Task<StripeCustomer?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(StripeCustomer customer, CancellationToken ct = default);
}
