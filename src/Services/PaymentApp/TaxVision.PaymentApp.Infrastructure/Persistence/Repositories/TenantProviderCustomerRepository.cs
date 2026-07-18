using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Domain.ProviderCustomers;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Repositories;

public sealed class TenantProviderCustomerRepository(PaymentAppDbContext db) : ITenantProviderCustomerRepository
{
    public Task<TenantProviderCustomer?> GetByTenantAndProviderAsync(
        Guid tenantId,
        PaymentProviderCode code,
        CancellationToken ct = default
    ) =>
        WithMethods(db.TenantProviderCustomers)
            .FirstOrDefaultAsync(customer => customer.TenantId == tenantId && customer.ProviderCode == code, ct);

    public Task<TenantProviderCustomer?> GetByIdAsync(
        Guid tenantProviderCustomerId,
        Guid tenantId,
        CancellationToken ct = default
    ) =>
        WithMethods(db.TenantProviderCustomers)
            .FirstOrDefaultAsync(
                customer => customer.Id == tenantProviderCustomerId && customer.TenantId == tenantId,
                ct
            );

    public async Task<IReadOnlyList<TenantProviderCustomer>> GetWithMethodsExpiringBeforeAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        await WithMethods(db.TenantProviderCustomers)
            .Where(customer =>
                customer.SavedMethods.Any(method =>
                    !method.IsDetached
                    && method.ExpiryNoticeSentAtUtc == null
                    && (
                        method.ExpYear < cutoffUtc.Year
                        || (method.ExpYear == cutoffUtc.Year && method.ExpMonth <= cutoffUtc.Month)
                    )
                )
            )
            .OrderBy(customer => customer.Id)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task AddAsync(TenantProviderCustomer customer, CancellationToken ct = default) =>
        await db.TenantProviderCustomers.AddAsync(customer, ct);

    private static IQueryable<TenantProviderCustomer> WithMethods(IQueryable<TenantProviderCustomer> query) =>
        query.Include(customer => customer.SavedMethods);
}
