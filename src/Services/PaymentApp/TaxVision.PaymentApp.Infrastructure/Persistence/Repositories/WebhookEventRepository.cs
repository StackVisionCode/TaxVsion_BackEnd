using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Domain.Webhooks;

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Repositories;

public sealed class WebhookEventRepository(PaymentAppDbContext db) : IWebhookEventRepository
{
    // IgnoreQueryFilters: dedup global de webhooks entrantes — el proveedor no conoce ni pasa
    // tenantId; providerCode+providerEventId es único global (unique index). Devuelve la fila
    // completa (no un bool) para que el caller distinga por estado: terminal ⇒ duplicado; no
    // terminal ⇒ quedó a medias y se re-procesa.
    public Task<WebhookEvent?> GetByProviderEventIdAsync(
        PaymentProviderCode code,
        string providerEventId,
        CancellationToken ct = default
    ) =>
        db
            .WebhookEvents.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.ProviderCode == code && e.ProviderEventId == providerEventId, ct);

    public async Task AddAsync(WebhookEvent webhookEvent, CancellationToken ct = default) =>
        await db.WebhookEvents.AddAsync(webhookEvent, ct);
}
