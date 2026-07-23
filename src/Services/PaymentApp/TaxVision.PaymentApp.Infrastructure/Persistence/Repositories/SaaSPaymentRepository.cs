using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Repositories;

public sealed class SaaSPaymentRepository(PaymentAppDbContext db) : ISaaSPaymentRepository
{
    // IgnoreQueryFilters: mismo bug/fix que CustomerReadService/SignatureAnalyticsReadService —
    // este repo puede correr dentro de un handler de Wolverine (bus.InvokeAsync) en un scope de DI
    // desconectado del que pobló ITenantContext vía JwtTenantContextMiddleware. tenantId ya viene
    // explícito y validado del caller (controller/JWT); el filtro explícito de abajo garantiza el
    // aislamiento sin depender del filtro ambiental roto.
    public Task<SaaSPayment?> GetByIdAsync(Guid saaSPaymentId, Guid tenantId, CancellationToken ct = default) =>
        WithChildren(db.SaaSPayments)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(payment => payment.Id == saaSPaymentId && payment.TenantId == tenantId, ct);

    public Task<SaaSPayment?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default)
    {
        var keyResult = IdempotencyKey.Create(idempotencyKey);
        if (keyResult.IsFailure)
            return Task.FromResult<SaaSPayment?>(null);

        var key = keyResult.Value;
        return WithChildren(db.SaaSPayments).FirstOrDefaultAsync(payment => payment.IdempotencyKey == key, ct);
    }

    public Task<SaaSPayment?> GetByExternalReferenceAsync(
        PaymentProviderCode code,
        string providerChargeReference,
        CancellationToken ct = default
    ) =>
        WithChildren(db.SaaSPayments)
            .FirstOrDefaultAsync(
                payment =>
                    payment.ExternalChargeReference != null
                    && payment.ExternalChargeReference.Provider == code
                    && payment.ExternalChargeReference.Value == providerChargeReference,
                ct
            );

    // IgnoreQueryFilters: job cross-tenant (RBAC Fase 5) — recorre pagos de todos los tenants
    // buscando reintentos/atascos vencidos, nunca sirve una request autenticada.
    public async Task<IReadOnlyList<SaaSPayment>> GetStuckProcessingAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        await WithChildren(db.SaaSPayments)
            .IgnoreQueryFilters()
            .Where(payment => payment.Status == PaymentStatus.Processing && payment.UpdatedAtUtc < cutoffUtc)
            .OrderBy(payment => payment.UpdatedAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SaaSPayment>> GetDueForRetryAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        await WithChildren(db.SaaSPayments)
            .IgnoreQueryFilters()
            .Where(payment =>
                payment.Status == PaymentStatus.Failed
                && payment.NextRetryAtUtc != null
                && payment.NextRetryAtUtc <= nowUtc
            )
            .OrderBy(payment => payment.NextRetryAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

    public Task<int> CountDueForRetryAsync(DateTime nowUtc, CancellationToken ct = default) =>
        db.SaaSPayments.CountAsync(
            payment =>
                payment.Status == PaymentStatus.Failed
                && payment.NextRetryAtUtc != null
                && payment.NextRetryAtUtc <= nowUtc,
            ct
        );

    public Task<long> SumSucceededAmountCentsAsync(
        SaaSPaymentType type,
        DateTime sinceUtc,
        CancellationToken ct = default
    ) =>
        db
            .SaaSPayments.Where(payment =>
                payment.Status == PaymentStatus.Succeeded && payment.Type == type && payment.PaidAtUtc >= sinceUtc
            )
            .SumAsync(payment => payment.Amount.AmountCents, ct);

    public async Task<IReadOnlyList<SaaSPayment>> SearchAdminAsync(
        Guid? tenantId,
        PaymentStatus? status,
        SaaSPaymentType? type,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        CancellationToken ct = default
    )
    {
        // IgnoreQueryFilters: búsqueda admin, tenantId opcional (null = todos los tenants).
        // Cuando se provee, el .Where explícito de abajo ya aísla por tenant; el filtro ambiental
        // no es confiable en este path (mismo bug documentado en LocalCommandTenantMiddleware).
        var query = WithChildren(db.SaaSPayments).AsNoTracking().IgnoreQueryFilters().AsQueryable();

        if (tenantId is not null)
            query = query.Where(payment => payment.TenantId == tenantId);

        if (status is not null)
            query = query.Where(payment => payment.Status == status);

        if (type is not null)
            query = query.Where(payment => payment.Type == type);

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

    public async Task AddAsync(SaaSPayment payment, CancellationToken ct = default) =>
        await db.SaaSPayments.AddAsync(payment, ct);

    private static IQueryable<SaaSPayment> WithChildren(IQueryable<SaaSPayment> query) =>
        query.Include(payment => payment.Attempts).Include(payment => payment.Refunds);
}
