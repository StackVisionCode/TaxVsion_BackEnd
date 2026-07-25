using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Domain.ProviderCustomers;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Repositories;

public sealed class TenantProviderCustomerRepository(PaymentAppDbContext db) : ITenantProviderCustomerRepository
{
    // IgnoreQueryFilters: mismo bug/fix que CustomerReadService/SignatureAnalyticsReadService —
    // este repo puede correr dentro de un handler de Wolverine (bus.InvokeAsync) en un scope de DI
    // desconectado del que pobló ITenantContext vía JwtTenantContextMiddleware. tenantId ya viene
    // explícito y validado del caller; el filtro explícito de abajo garantiza el aislamiento.
    public Task<TenantProviderCustomer?> GetByTenantAndProviderAsync(
        Guid tenantId,
        PaymentProviderCode code,
        CancellationToken ct = default
    ) =>
        WithMethods(db.TenantProviderCustomers)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(customer => customer.TenantId == tenantId && customer.ProviderCode == code, ct);

    public Task<TenantProviderCustomer?> GetByIdAsync(
        Guid tenantProviderCustomerId,
        Guid tenantId,
        CancellationToken ct = default
    ) =>
        WithMethods(db.TenantProviderCustomers)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                customer => customer.Id == tenantProviderCustomerId && customer.TenantId == tenantId,
                ct
            );

    // IgnoreQueryFilters: job cross-tenant (RBAC Fase 5) — barre métodos de pago por vencer
    // en todos los tenants, nunca sirve una request autenticada.
    public async Task<IReadOnlyList<TenantProviderCustomer>> GetWithMethodsExpiringBeforeAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        await WithMethods(db.TenantProviderCustomers)
            .IgnoreQueryFilters()
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
