using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Imports;

namespace TaxVision.Customer.Infrastructure.Persistence.Repositories;

internal sealed class CustomerImportRepository(CustomerDbContext db) : ICustomerImportRepository
{
    public Task<CustomerImportAttempt?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.CustomerImportAttempts.Include(a => a.Rows).FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<CustomerImportAttempt?> GetByIdempotencyKeyAsync(
        Guid tenantId,
        string idempotencyKey,
        CancellationToken ct
    ) =>
        db.CustomerImportAttempts.FirstOrDefaultAsync(
            a => a.TenantId == tenantId && a.IdempotencyKey == idempotencyKey,
            ct
        );

    public Task<int> CountActiveByTenantAsync(Guid tenantId, CancellationToken ct) =>
        db.CustomerImportAttempts.CountAsync(
            a =>
                a.TenantId == tenantId
                && a.Status != ImportStatus.Completed
                && a.Status != ImportStatus.Failed
                && a.Status != ImportStatus.Canceled,
            ct
        );

    public async Task AddAsync(CustomerImportAttempt attempt, CancellationToken ct)
    {
        await db.CustomerImportAttempts.AddAsync(attempt, ct);
    }
}
