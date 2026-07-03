using Microsoft.EntityFrameworkCore;
using TaxVision.Payment.Application.Abstractions;
using TaxVision.Payment.Domain.SaaSPayments;
using TaxVision.Payment.Infrastructure.Persistence;

namespace TaxVision.Payment.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ISaaSPaymentRepository"/>.
/// Uses <see cref="PaymentDbContext"/> as the unit of work; changes are saved by the caller.
/// </summary>
public sealed class SaaSPaymentRepository(PaymentDbContext dbContext) : ISaaSPaymentRepository
{
    /// <inheritdoc/>
    public async Task<SaaSPayment?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await dbContext.SaaSPayments.FirstOrDefaultAsync(p => p.Id == id, ct);

    /// <inheritdoc/>
    public async Task<SaaSPayment?> GetByReferenceIdAsync(Guid referenceId, SaaSPaymentType type, CancellationToken ct = default)
        => await dbContext.SaaSPayments
            .FirstOrDefaultAsync(p => p.ReferenceId == referenceId && p.PaymentType == type, ct);

    /// <inheritdoc/>
    public async Task<SaaSPayment?> GetByStripePaymentIntentIdAsync(string paymentIntentId, CancellationToken ct = default)
        => await dbContext.SaaSPayments
            .FirstOrDefaultAsync(p => p.StripePaymentIntentId == paymentIntentId, ct);

    /// <inheritdoc/>
    public async Task AddAsync(SaaSPayment payment, CancellationToken ct = default)
        => await dbContext.SaaSPayments.AddAsync(payment, ct);

    /// <inheritdoc/>
    public async Task<List<SaaSPayment>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default)
        => await dbContext.SaaSPayments
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(ct);
}
