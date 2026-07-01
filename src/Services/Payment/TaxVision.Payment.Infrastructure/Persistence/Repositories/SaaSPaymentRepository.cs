using Microsoft.EntityFrameworkCore;
using TaxVision.Payment.Application.Abstractions;
using TaxVision.Payment.Domain.SaaSPayments;
using TaxVision.Payment.Infrastructure.Persistence;

namespace TaxVision.Payment.Infrastructure.Persistence.Repositories;

public sealed class SaaSPaymentRepository(PaymentDbContext dbContext) : ISaaSPaymentRepository
{
    public async Task<SaaSPayment?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await dbContext.SaaSPayments.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<SaaSPayment?> GetByReferenceIdAsync(Guid referenceId, SaaSPaymentType type, CancellationToken ct = default)
        => await dbContext.SaaSPayments
            .FirstOrDefaultAsync(p => p.ReferenceId == referenceId && p.PaymentType == type, ct);

    public async Task AddAsync(SaaSPayment payment, CancellationToken ct = default)
        => await dbContext.SaaSPayments.AddAsync(payment, ct);

    public async Task<List<SaaSPayment>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default)
        => await dbContext.SaaSPayments
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(ct);
}
