using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Domain.Webhooks;

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Repositories;

public sealed class WebhookEventRepository(PaymentAppDbContext db) : IWebhookEventRepository
{
    public Task<bool> ExistsAsync(PaymentProviderCode code, string providerEventId, CancellationToken ct = default) =>
        db.WebhookEvents.AnyAsync(e => e.ProviderCode == code && e.ProviderEventId == providerEventId, ct);

    public async Task AddAsync(WebhookEvent webhookEvent, CancellationToken ct = default) =>
        await db.WebhookEvents.AddAsync(webhookEvent, ct);
}
