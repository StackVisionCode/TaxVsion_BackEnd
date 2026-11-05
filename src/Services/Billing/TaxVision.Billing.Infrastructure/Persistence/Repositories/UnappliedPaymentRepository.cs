using Microsoft.EntityFrameworkCore;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Domain.Payments;

namespace TaxVision.Billing.Infrastructure.Persistence.Repositories;

// IgnoreQueryFilters + tenant explícito: se usa desde el consumidor (scope de Wolverine sin tenant ambiental).
public sealed class UnappliedPaymentRepository(BillingDbContext dbContext) : IUnappliedPaymentRepository
{
    public Task<bool> ExistsAsync(Guid tenantId, Guid tenantPaymentId, CancellationToken ct = default) =>
        dbContext
            .UnappliedPayments.IgnoreQueryFilters()
            .AnyAsync(p => p.TenantId == tenantId && p.TenantPaymentId == tenantPaymentId, ct);

    public Task<UnappliedPayment?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        dbContext
            .UnappliedPayments.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == id, ct);

    public async Task<IReadOnlyList<UnappliedPayment>> ListByTenantAsync(
        Guid tenantId,
        bool includeResolved,
        int take,
        CancellationToken ct = default
    ) =>
        await dbContext
            .UnappliedPayments.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && (includeResolved || p.ResolvedAtUtc == null))
            .OrderByDescending(p => p.ReceivedAtUtc)
            .Take(take)
            .ToListAsync(ct);

    public async Task AddAsync(UnappliedPayment payment, CancellationToken ct = default) =>
        await dbContext.UnappliedPayments.AddAsync(payment, ct);
}
