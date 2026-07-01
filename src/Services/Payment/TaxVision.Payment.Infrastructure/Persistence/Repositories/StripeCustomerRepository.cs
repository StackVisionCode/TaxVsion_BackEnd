using Microsoft.EntityFrameworkCore;
using TaxVision.Payment.Application.Abstractions;
using TaxVision.Payment.Domain.StripeCustomers;
using TaxVision.Payment.Infrastructure.Persistence;

namespace TaxVision.Payment.Infrastructure.Persistence.Repositories;

public sealed class StripeCustomerRepository(PaymentDbContext dbContext) : IStripeCustomerRepository
{
    public async Task<StripeCustomer?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default)
        => await dbContext.StripeCustomers.FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);

    public async Task AddAsync(StripeCustomer customer, CancellationToken ct = default)
        => await dbContext.StripeCustomers.AddAsync(customer, ct);
}
