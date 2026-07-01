using Microsoft.EntityFrameworkCore;
using TaxVision.Payment.Application.Abstractions;
using TaxVision.Payment.Domain.TenantPayments;
using TaxVision.Payment.Infrastructure.Persistence;

namespace TaxVision.Payment.Infrastructure.Persistence.Repositories;

public sealed class TenantTransactionRepository(PaymentDbContext dbContext) : ITenantTransactionRepository
{
    public async Task<TenantTransaction?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await dbContext.TenantTransactions.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<List<TenantTransaction>> GetByTenantAsync(Guid tenantId, int page, int size, CancellationToken ct = default)
        => await dbContext.TenantTransactions
            .Where(t => t.TenantId == tenantId)
            .OrderByDescending(t => t.CreatedAtUtc)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);

    public async Task AddAsync(TenantTransaction transaction, CancellationToken ct = default)
        => await dbContext.TenantTransactions.AddAsync(transaction, ct);
}
