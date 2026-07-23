using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Domain.Webhooks;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

public sealed class WebhookEventRepository(PaymentClientDbContext db) : IWebhookEventRepository
{
    // IgnoreQueryFilters: este repo corre dentro de un handler de Wolverine (bus.InvokeAsync),
    // en un scope de DI distinto al de la request HTTP que pobló ITenantContext vía
    // JwtTenantContextMiddleware; el HasQueryFilter ambiental de PaymentClientDbContext ve
    // Guid.Empty ahí. tenantId ya viene explícito y validado desde el evento de webhook.
    public Task<bool> ExistsAsync(
        Guid tenantId,
        PaymentProviderCode code,
        string providerEventId,
        CancellationToken ct = default
    ) =>
        db
            .WebhookEvents.IgnoreQueryFilters()
            .AnyAsync(
                e => e.TenantId == tenantId && e.ProviderCode == code && e.ProviderEventId == providerEventId,
                ct
            );

    public async Task AddAsync(WebhookEvent webhookEvent, CancellationToken ct = default) =>
        await db.WebhookEvents.AddAsync(webhookEvent, ct);
}
