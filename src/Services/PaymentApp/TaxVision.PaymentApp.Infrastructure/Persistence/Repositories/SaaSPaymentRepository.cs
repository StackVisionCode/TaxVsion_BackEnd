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

    // IgnoreQueryFilters: PayFlow (Fase 17) — OnboardingRefundConsumer solo conoce el PaymentId,
    // no el tenant (el pago pudo crearse con TenantId=Guid.Empty vía CreateForOnboarding, y el
    // evento de refund lo publica Auth sin ese dato de todos modos).
    public Task<SaaSPayment?> GetByIdAsync(Guid saaSPaymentId, CancellationToken ct = default) =>
        WithChildren(db.SaaSPayments)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(payment => payment.Id == saaSPaymentId, ct);

    // IgnoreQueryFilters: dedup global por IdempotencyKey (Stripe payment intent creation) —
    // el propósito es encontrar un pago existente SIN conocer el tenant todavía. Sin esto, un
    // reintento idempotente creaba un pago DUPLICADO cada vez que llegaba dentro del scope
    // Wolverine (ChargeSaaSPaymentHandler) porque el fetch siempre devolvía null.
    public Task<SaaSPayment?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default)
    {
        var keyResult = IdempotencyKey.Create(idempotencyKey);
        if (keyResult.IsFailure)
            return Task.FromResult<SaaSPayment?>(null);

        var key = keyResult.Value;
        return WithChildren(db.SaaSPayments)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(payment => payment.IdempotencyKey == key, ct);
    }

    // IgnoreQueryFilters: lookup de webhook entrante de Stripe (ProcessStripeWebhookHandler) —
    // Stripe no conoce ni pasa el tenantId; el providerChargeReference es único global. Sin
    // esto, TODOS los webhooks de Stripe caían en "payment not found" y no actualizaban estado.
    public Task<SaaSPayment?> GetByExternalReferenceAsync(
        PaymentProviderCode code,
        string providerChargeReference,
        CancellationToken ct = default
    ) =>
        WithChildren(db.SaaSPayments)
            .IgnoreQueryFilters()
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

    // Mismo criterio cross-tenant que GetStuckProcessingAsync. El pago del onboarding queda fuera solo,
    // porque nace sin tenant: su recibo lo pide Auth.
    public async Task<IReadOnlyList<SaaSPayment>> GetSucceededWithoutReceiptAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        await db
            .SaaSPayments.IgnoreQueryFilters()
            .Where(payment =>
                payment.Status == PaymentStatus.Succeeded
                && payment.TenantId != Guid.Empty
                && payment.ReceiptFileId == null
                && payment.PaidAtUtc != null
                && payment.PaidAtUtc < cutoffUtc
            )
            .OrderBy(payment => payment.PaidAtUtc)
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

    // IgnoreQueryFilters: métrica cross-tenant (PaymentAppMetrics observable gauge, sin request
    // HTTP asociada — ITenantContext vacío por diseño).
    public Task<int> CountDueForRetryAsync(DateTime nowUtc, CancellationToken ct = default) =>
        db
            .SaaSPayments.IgnoreQueryFilters()
            .CountAsync(
                payment =>
                    payment.Status == PaymentStatus.Failed
                    && payment.NextRetryAtUtc != null
                    && payment.NextRetryAtUtc <= nowUtc,
                ct
            );

    // IgnoreQueryFilters: métrica cross-tenant (PaymentAppMetrics revenue by type).
    public Task<long> SumSucceededAmountCentsAsync(
        SaaSPaymentType type,
        DateTime sinceUtc,
        CancellationToken ct = default
    ) =>
        db
            .SaaSPayments.IgnoreQueryFilters()
            .Where(payment =>
                payment.Status == PaymentStatus.Succeeded && payment.Type == type && payment.PaidAtUtc >= sinceUtc
            )
            .SumAsync(payment => payment.Amount.AmountCents, ct);

    // IgnoreQueryFilters: hasta que la saga crea el tenant, la fila vive con TenantId = Guid.Empty y el
    // filtro ambiental no la alcanzaría.
    public Task<SaaSPayment?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
        db.SaaSPayments.IgnoreQueryFilters().FirstOrDefaultAsync(payment => payment.OnboardingId == onboardingId, ct);

    // Solo refunds: los Attempts traen el cuerpo crudo del proveedor y no salen del lado admin. El .Where
    // explícito por tenant es lo que aísla, igual que en el resto de lecturas.
    public async Task<(IReadOnlyList<SaaSPayment> Items, int TotalCount)> SearchForTenantAsync(
        Guid tenantId,
        int page,
        int pageSize,
        CancellationToken ct = default
    )
    {
        var query = db.SaaSPayments.AsNoTracking().IgnoreQueryFilters().Where(payment => payment.TenantId == tenantId);

        var total = await query.CountAsync(ct);
        var items = await query
            .Include(payment => payment.Refunds)
            .OrderByDescending(payment => payment.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

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
