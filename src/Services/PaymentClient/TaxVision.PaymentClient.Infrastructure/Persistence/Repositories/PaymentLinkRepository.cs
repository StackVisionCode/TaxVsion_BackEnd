using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Domain.PaymentLinks;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

public sealed class PaymentLinkRepository(PaymentClientDbContext db) : IPaymentLinkRepository
{
    // IgnoreQueryFilters: este repo corre dentro de un handler de Wolverine (bus.InvokeAsync),
    // en un scope de DI distinto al de la request HTTP que pobló ITenantContext vía
    // JwtTenantContextMiddleware; el HasQueryFilter ambiental de PaymentClientDbContext ve
    // Guid.Empty ahí. tenantId ya viene explícito y validado desde el controller/evento.
    public Task<PaymentLink?> GetByIdAsync(Guid paymentLinkId, Guid tenantId, CancellationToken ct = default) =>
        db
            .PaymentLinks.IgnoreQueryFilters()
            .FirstOrDefaultAsync(link => link.Id == paymentLinkId && link.TenantId == tenantId, ct);

    // link.Token.Value == token no traduce a SQL: Token está mapeado como value converter
    // (columna escalar), no como owned type, así que EF no puede navegar ".Value" sobre el
    // objeto CLR — hay que comparar el VO completo para que aplique el converter en ambos lados.
    // IgnoreQueryFilters: lookup tenant-agnóstico deliberado — el checkout público solo tiene el
    // token, el tenant se deriva del link encontrado (ver IPaymentLinkRepository XML doc). El
    // token es un secreto opaco no adivinable. Sin esto, el checkout público siempre daba 404.
    public Task<PaymentLink?> GetByTokenAsync(string token, CancellationToken ct = default)
    {
        var tokenResult = PaymentLinkToken.FromExisting(token);
        if (tokenResult.IsFailure)
            return Task.FromResult<PaymentLink?>(null);

        return db.PaymentLinks.IgnoreQueryFilters().FirstOrDefaultAsync(link => link.Token == tokenResult.Value, ct);
    }

    // IgnoreQueryFilters: reverse lookup dentro del webhook handler (ProcessTenantWebhookHandler)
    // que ya validó el tenant del payment padre — el tenantPaymentId viene de un TenantPayment ya
    // resuelto en el mismo scope. Sin esto, el webhook nunca encontraba el link relacionado y no
    // podía actualizar su estado.
    public Task<PaymentLink?> GetByRelatedTenantPaymentIdAsync(Guid tenantPaymentId, CancellationToken ct = default) =>
        db
            .PaymentLinks.IgnoreQueryFilters()
            .FirstOrDefaultAsync(link => link.RelatedTenantPaymentId == tenantPaymentId, ct);

    public async Task<IReadOnlyList<PaymentLink>> SearchByTenantAsync(
        Guid tenantId,
        PaymentLinkStatus? status,
        int page,
        int pageSize,
        CancellationToken ct = default
    )
    {
        var query = db.PaymentLinks.AsNoTracking().IgnoreQueryFilters().Where(link => link.TenantId == tenantId);

        if (status is not null)
            query = query.Where(link => link.Status == status);

        return await query
            .OrderByDescending(link => link.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    // IgnoreQueryFilters: job cross-tenant (RBAC Fase 5) — expira PaymentLinks vencidos de
    // todos los tenants, nunca sirve una request autenticada.
    public async Task<IReadOnlyList<PaymentLink>> GetActiveExpiredBeforeAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        await db
            .PaymentLinks.IgnoreQueryFilters()
            .Where(link => link.Status == PaymentLinkStatus.Active && link.ExpiresAtUtc <= cutoffUtc)
            .OrderBy(link => link.ExpiresAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task AddAsync(PaymentLink link, CancellationToken ct = default) =>
        await db.PaymentLinks.AddAsync(link, ct);
}
