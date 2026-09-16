using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Domain.Webhooks;

namespace TaxVision.PaymentApp.Application.Abstractions;

public interface IWebhookEventRepository
{
    /// <summary>Devuelve el evento ya registrado para <c>(code, providerEventId)</c>, o null si es la
    /// primera entrega. El caller decide por su estado: terminal ⇒ duplicado a descartar; no terminal
    /// ⇒ quedó a medias y se re-procesa (ver <see cref="WebhookEvent.IsTerminal"/>).</summary>
    Task<WebhookEvent?> GetByProviderEventIdAsync(
        PaymentProviderCode code,
        string providerEventId,
        CancellationToken ct = default
    );
    Task AddAsync(WebhookEvent webhookEvent, CancellationToken ct = default);
}
