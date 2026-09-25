namespace TaxVision.PaymentApp.Application.Abstractions;

/// <summary>
/// <c>PaymentAttemptThrottlePolicy</c> (§41.4 del diseño) — throttling a nivel de dominio,
/// respaldado por Redis, complementario al rate limiting HTTP del gateway/middleware. Un
/// tenant que dispara webhooks o acciones admin muy por encima de lo normal probablemente
/// esté siendo abusado (o el provider tiene un bug de reintento), no es tráfico legítimo.
/// </summary>
public interface IPaymentAttemptThrottle
{
    /// <summary>60 webhooks por minuto por <paramref name="scopeId"/>: el tenant del pago, o su
    /// onboarding mientras el tenant todavía no existe.</summary>
    Task<bool> IsWebhookThrottledAsync(Guid scopeId, CancellationToken ct = default);

    Task RegisterWebhookAttemptAsync(Guid scopeId, CancellationToken ct = default);

    /// <summary><c>MaxAdminActionAttemptsPerMinutePerTenant = 5</c> — reembolsos u otras
    /// acciones administrativas de dinero sobre el mismo tenant.</summary>
    Task<bool> IsAdminActionThrottledAsync(Guid tenantId, CancellationToken ct = default);

    Task RegisterAdminActionAttemptAsync(Guid tenantId, CancellationToken ct = default);
}
