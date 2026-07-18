using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Domain.Webhooks;

namespace TaxVision.PaymentApp.Application.Abstractions;

public interface IWebhookEventRepository
{
    Task<bool> ExistsAsync(PaymentProviderCode code, string providerEventId, CancellationToken ct = default);
    Task AddAsync(WebhookEvent webhookEvent, CancellationToken ct = default);
}
