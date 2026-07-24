using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Imports;

namespace TaxVision.Customer.Infrastructure.Persistence.Repositories;

internal sealed class CustomerImportRepository(CustomerDbContext db) : ICustomerImportRepository
{
    // IgnoreQueryFilters(): mismo bug que UserRepository.GetByIdAsync (Auth) — corre en Wolverine
    // handlers/consumers (bus.InvokeAsync) donde el ITenantContext ambiente puede llegar vacío.
    // Es seguro: los ~9 llamadores (ImportFileScanResultConsumer 3×, RunCustomerImportHandler 6×,
    // CancelCustomerImportHandler) validan post-fetch (attempt.TenantId != msg.TenantId) o
    // reciben el attemptId desde un mensaje interno ya validado en el paso previo del pipeline.
    public Task<CustomerImportAttempt?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.CustomerImportAttempts.IgnoreQueryFilters().Include(a => a.Rows).FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<CustomerImportAttempt?> GetByIdempotencyKeyAsync(
        Guid tenantId,
        string idempotencyKey,
        CancellationToken ct
    ) =>
        db
            .CustomerImportAttempts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.IdempotencyKey == idempotencyKey, ct);

    public Task<int> CountActiveByTenantAsync(Guid tenantId, CancellationToken ct) =>
        db
            .CustomerImportAttempts.IgnoreQueryFilters()
            .CountAsync(
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
