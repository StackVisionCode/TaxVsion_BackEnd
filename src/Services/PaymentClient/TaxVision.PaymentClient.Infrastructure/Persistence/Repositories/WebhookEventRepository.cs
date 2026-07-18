using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Domain.Webhooks;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

public sealed class WebhookEventRepository(PaymentClientDbContext db) : IWebhookEventRepository
{
    public Task<bool> ExistsAsync(Guid tenantId, PaymentProviderCode code, string providerEventId, CancellationToken ct = default) =>
        db.WebhookEvents.AnyAsync(e => e.TenantId == tenantId && e.ProviderCode == code && e.ProviderEventId == providerEventId, ct);

    public async Task AddAsync(WebhookEvent webhookEvent, CancellationToken ct = default) =>
        await db.WebhookEvents.AddAsync(webhookEvent, ct);
}
