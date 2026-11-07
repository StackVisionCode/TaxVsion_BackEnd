using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

public sealed class TenantPaymentRepository(PaymentClientDbContext db) : ITenantPaymentRepository
{
    // IgnoreQueryFilters: este repo corre dentro de un handler de Wolverine (bus.InvokeAsync),
    // en un scope de DI distinto al de la request HTTP que pobló ITenantContext vía
    // JwtTenantContextMiddleware; el HasQueryFilter ambiental de PaymentClientDbContext ve
    // Guid.Empty ahí. tenantId ya viene explícito y validado desde el controller/evento.
    public Task<TenantPayment?> GetByIdAsync(Guid tenantPaymentId, Guid tenantId, CancellationToken ct = default) =>
        WithChildren(db.TenantPayments)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(payment => payment.Id == tenantPaymentId && payment.TenantId == tenantId, ct);

    public Task<TenantPayment?> GetByIdempotencyKeyAsync(
        Guid tenantId,
        string idempotencyKey,
        CancellationToken ct = default
    )
    {
        var keyResult = IdempotencyKey.Create(idempotencyKey);
        if (keyResult.IsFailure)
            return Task.FromResult<TenantPayment?>(null);

        var key = keyResult.Value;
        return WithChildren(db.TenantPayments)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(payment => payment.TenantId == tenantId && payment.IdempotencyKey == key, ct);
    }

    public Task<TenantPayment?> GetByExternalReferenceAsync(
        Guid tenantId,
        PaymentProviderCode code,
        string providerChargeReference,
        CancellationToken ct = default
    ) =>
        WithChildren(db.TenantPayments)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                payment =>
                    payment.TenantId == tenantId
                    && payment.ExternalChargeReference != null
                    && payment.ExternalChargeReference.Provider == code
                    && payment.ExternalChargeReference.Value == providerChargeReference,
                ct
            );

    // IgnoreQueryFilters: jobs cross-tenant (RBAC Fase 5) — barren pagos atascados/vencidos de
    // todos los tenants, nunca sirven una request autenticada.
    public async Task<IReadOnlyList<AwaitingProviderPayment>> GetAwaitingProviderConfirmationAsync(
        DateTime stuckBeforeUtc,
        DateTime createdAfterUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        await db
            .TenantPayments.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(payment =>
                (
                    payment.Status == PaymentStatus.Processing
                    || payment.Status == PaymentStatus.RequiresAction
                    || payment.Status == PaymentStatus.Pending
                )
                && payment.UpdatedAtUtc < stuckBeforeUtc
                && payment.CreatedAtUtc > createdAfterUtc
            )
            // NULL va primero en orden ascendente: los nunca consultados antes que el resto.
            .OrderBy(payment => payment.LastProviderCheckAtUtc)
            .ThenBy(payment => payment.UpdatedAtUtc)
            .Take(batchSize)
            .Select(payment => new AwaitingProviderPayment(payment.Id, payment.TenantId))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TenantPayment>> GetDueForRetryAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        await WithChildren(db.TenantPayments)
            .IgnoreQueryFilters()
            .Where(payment =>
                payment.Status == PaymentStatus.Failed
                && payment.NextRetryAtUtc != null
                && payment.NextRetryAtUtc <= nowUtc
            )
            .OrderBy(payment => payment.NextRetryAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TenantPayment>> SearchAdminAsync(
        Guid? tenantId,
        PaymentStatus? status,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        CancellationToken ct = default
    )
    {
        // IgnoreQueryFilters() deliberado: este endpoint es cross-tenant por diseño (§42.6,
        // ver PaymentClientAdminController) — gateado por AllowActorTypes(PlatformAdmin) +
        // HasPermission(AdminCrossTenant), no por pertenencia a un tenant. Sin esto, además de
        // sufrir el mismo bug de scope de Wolverine que el resto del repo, la rama tenantId=null
        // (búsqueda explícitamente cross-tenant) quedaría rota incluso si se arreglara la
        // propagación del TenantContext, porque no hay ningún tenantId que comparar.
        var query = WithChildren(db.TenantPayments).AsNoTracking().IgnoreQueryFilters().AsQueryable();

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

    public Task<bool> AnyForProviderAsync(Guid tenantId, PaymentProviderCode code, CancellationToken ct = default) =>
        db
            .TenantPayments.IgnoreQueryFilters()
            .AnyAsync(payment => payment.TenantId == tenantId && payment.ProviderCode == code, ct);

    // Un EXISTS por config de la oficina (pocas filas), cortado en el primer pago: sin N+1 ni contar pagos.
    public async Task<IReadOnlySet<PaymentProviderCode>> GetConfiguredProvidersWithPaymentsAsync(
        Guid tenantId,
        CancellationToken ct = default
    )
    {
        var codes = await db
            .TenantPaymentConfigs.IgnoreQueryFilters()
            .Where(config => config.TenantId == tenantId)
            .Where(config =>
                db.TenantPayments.IgnoreQueryFilters()
                    .Any(payment => payment.TenantId == tenantId && payment.ProviderCode == config.ProviderCode)
            )
            .Select(config => config.ProviderCode)
            .ToListAsync(ct);

        return codes.ToHashSet();
    }

    public Task<bool> AnyInProgressForReferenceAsync(
        Guid tenantId,
        string externalReferenceId,
        CancellationToken ct = default
    ) =>
        db
            .TenantPayments.IgnoreQueryFilters()
            .AnyAsync(
                payment =>
                    payment.TenantId == tenantId
                    && payment.Purpose.ExternalReferenceId == externalReferenceId
                    && (
                        payment.Status == PaymentStatus.Pending
                        || payment.Status == PaymentStatus.Processing
                        || payment.Status == PaymentStatus.RequiresAction
                    ),
                ct
            );

    public Task<bool> AnySucceededForReferenceSinceAsync(
        Guid tenantId,
        string externalReferenceId,
        DateTime sinceUtc,
        CancellationToken ct = default
    ) =>
        db
            .TenantPayments.IgnoreQueryFilters()
            .AnyAsync(
                payment =>
                    payment.TenantId == tenantId
                    && payment.Purpose.ExternalReferenceId == externalReferenceId
                    && payment.Status == PaymentStatus.Succeeded
                    && payment.PaidAtUtc > sinceUtc,
                ct
            );

    // IgnoreQueryFilters: reposición de plataforma, cross-tenant por diseño.
    public async Task<IReadOnlyList<TenantPayment>> GetByIdsAcrossTenantsAsync(
        IReadOnlyCollection<Guid> tenantPaymentIds,
        CancellationToken ct = default
    ) =>
        await db
            .TenantPayments.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(payment => tenantPaymentIds.Contains(payment.Id))
            .ToListAsync(ct);

    public async Task AddAsync(TenantPayment payment, CancellationToken ct = default) =>
        await db.TenantPayments.AddAsync(payment, ct);

    private static IQueryable<TenantPayment> WithChildren(IQueryable<TenantPayment> query) =>
        query.Include(payment => payment.Attempts).Include(payment => payment.Refunds);
}
