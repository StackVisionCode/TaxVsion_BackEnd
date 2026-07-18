using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

public sealed class TenantPaymentRepository(PaymentClientDbContext db) : ITenantPaymentRepository
{
    public Task<TenantPayment?> GetByIdAsync(Guid tenantPaymentId, Guid tenantId, CancellationToken ct = default) =>
        WithChildren(db.TenantPayments)
            .FirstOrDefaultAsync(payment => payment.Id == tenantPaymentId && payment.TenantId == tenantId, ct);

    public Task<TenantPayment?> GetByIdempotencyKeyAsync(Guid tenantId, string idempotencyKey, CancellationToken ct = default)
    {
        var keyResult = IdempotencyKey.Create(idempotencyKey);
        if (keyResult.IsFailure)
            return Task.FromResult<TenantPayment?>(null);

        var key = keyResult.Value;
        return WithChildren(db.TenantPayments)
            .FirstOrDefaultAsync(payment => payment.TenantId == tenantId && payment.IdempotencyKey == key, ct);
    }

    public Task<TenantPayment?> GetByExternalReferenceAsync(
        Guid tenantId, PaymentProviderCode code, string providerChargeReference, CancellationToken ct = default) =>
        WithChildren(db.TenantPayments)
            .FirstOrDefaultAsync(payment =>
                payment.TenantId == tenantId
                && payment.ExternalChargeReference != null
                && payment.ExternalChargeReference.Provider == code
                && payment.ExternalChargeReference.Value == providerChargeReference, ct);

    public async Task<IReadOnlyList<TenantPayment>> GetStuckProcessingAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct = default) =>
        await WithChildren(db.TenantPayments)
            .Where(payment => payment.Status == PaymentStatus.Processing && payment.UpdatedAtUtc < cutoffUtc)
            .OrderBy(payment => payment.UpdatedAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TenantPayment>> GetDueForRetryAsync(DateTime nowUtc, int batchSize, CancellationToken ct = default) =>
        await WithChildren(db.TenantPayments)
            .Where(payment => payment.Status == PaymentStatus.Failed && payment.NextRetryAtUtc != null && payment.NextRetryAtUtc <= nowUtc)
            .OrderBy(payment => payment.NextRetryAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TenantPayment>> SearchAdminAsync(
        Guid? tenantId, PaymentStatus? status, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct = default)
    {
        var query = WithChildren(db.TenantPayments).AsNoTracking().AsQueryable();

        if (tenantId is not null)
            query = query.Where(payment => payment.TenantId == tenantId);

        if (status is not null)
            query = query.Where(payment => payment.Status == status);

        if (from is not null)
            query = query.Where(payment => payment.CreatedAtUtc >= from);

        if (to is not null)
            query = query.Where(payment => payment.CreatedAtUtc <= to);

        return await query
            .OrderByDescending(payment => payment.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task AddAsync(TenantPayment payment, CancellationToken ct = default) =>
        await db.TenantPayments.AddAsync(payment, ct);

    private static IQueryable<TenantPayment> WithChildren(IQueryable<TenantPayment> query) =>
        query.Include(payment => payment.Attempts).Include(payment => payment.Refunds);
}
