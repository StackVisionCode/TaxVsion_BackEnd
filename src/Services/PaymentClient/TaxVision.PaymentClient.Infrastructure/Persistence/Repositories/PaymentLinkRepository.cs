using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Domain.PaymentLinks;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

public sealed class PaymentLinkRepository(PaymentClientDbContext db) : IPaymentLinkRepository
{
    public Task<PaymentLink?> GetByIdAsync(Guid paymentLinkId, Guid tenantId, CancellationToken ct = default) =>
        db.PaymentLinks.FirstOrDefaultAsync(link => link.Id == paymentLinkId && link.TenantId == tenantId, ct);

    // link.Token.Value == token no traduce a SQL: Token está mapeado como value converter
    // (columna escalar), no como owned type, así que EF no puede navegar ".Value" sobre el
    // objeto CLR — hay que comparar el VO completo para que aplique el converter en ambos lados.
    public Task<PaymentLink?> GetByTokenAsync(string token, CancellationToken ct = default)
    {
        var tokenResult = PaymentLinkToken.FromExisting(token);
        if (tokenResult.IsFailure)
            return Task.FromResult<PaymentLink?>(null);

        return db.PaymentLinks.FirstOrDefaultAsync(link => link.Token == tokenResult.Value, ct);
    }

    public Task<PaymentLink?> GetByRelatedTenantPaymentIdAsync(Guid tenantPaymentId, CancellationToken ct = default) =>
        db.PaymentLinks.FirstOrDefaultAsync(link => link.RelatedTenantPaymentId == tenantPaymentId, ct);

    public async Task<IReadOnlyList<PaymentLink>> SearchByTenantAsync(
        Guid tenantId, PaymentLinkStatus? status, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.PaymentLinks.AsNoTracking().Where(link => link.TenantId == tenantId);

        if (status is not null)
            query = query.Where(link => link.Status == status);

        return await query
            .OrderByDescending(link => link.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PaymentLink>> GetActiveExpiredBeforeAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct = default) =>
        await db.PaymentLinks
            .Where(link => link.Status == PaymentLinkStatus.Active && link.ExpiresAtUtc <= cutoffUtc)
            .OrderBy(link => link.ExpiresAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task AddAsync(PaymentLink link, CancellationToken ct = default) =>
        await db.PaymentLinks.AddAsync(link, ct);
}
