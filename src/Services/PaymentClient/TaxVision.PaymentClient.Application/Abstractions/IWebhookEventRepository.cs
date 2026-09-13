using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Domain.Webhooks;

namespace TaxVision.PaymentClient.Application.Abstractions;

public interface IWebhookEventRepository
{
    /// <summary>Devuelve el evento ya registrado para <c>(tenantId, code, providerEventId)</c>, o null
    /// si es la primera entrega. El caller decide por su estado: terminal ⇒ duplicado a descartar; no
    /// terminal ⇒ quedó a medias y se re-procesa (ver <see cref="WebhookEvent.IsTerminal"/>).</summary>
    Task<WebhookEvent?> GetByProviderEventIdAsync(
        Guid tenantId,
        PaymentProviderCode code,
        string providerEventId,
        CancellationToken ct = default
    );
    Task AddAsync(WebhookEvent webhookEvent, CancellationToken ct = default);
}
