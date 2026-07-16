using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Domain.Webhooks;

namespace TaxVision.PaymentClient.Application.Abstractions;

public interface IWebhookEventRepository
{
    Task<bool> ExistsAsync(Guid tenantId, PaymentProviderCode code, string providerEventId, CancellationToken ct = default);
    Task AddAsync(WebhookEvent webhookEvent, CancellationToken ct = default);
}
