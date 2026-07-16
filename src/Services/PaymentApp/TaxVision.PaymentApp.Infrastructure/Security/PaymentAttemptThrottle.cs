using BuildingBlocks.Caching;
using TaxVision.PaymentApp.Application.Abstractions;

namespace TaxVision.PaymentApp.Infrastructure.Security;

/// <summary>
/// Implementación de <see cref="IPaymentAttemptThrottle"/> respaldada por Redis (vía
/// <see cref="ICacheService"/>) — mismo patrón que <c>Auth.Infrastructure.Security.LoginThrottler</c>:
/// contador por ventana fija de 1 minuto, no estrictamente atómico (aceptable para este
/// propósito de defensa en profundidad, no es el rate limit primario).
/// </summary>
public sealed class PaymentAttemptThrottle(ICacheService cache) : IPaymentAttemptThrottle
{
    private const int MaxWebhookAttemptsPerMinutePerTenant = 60;
    private const int MaxAdminActionAttemptsPerMinutePerTenant = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public async Task<bool> IsWebhookThrottledAsync(Guid tenantId, CancellationToken ct = default) =>
        await cache.GetAsync<int?>(WebhookKey(tenantId), ct) >= MaxWebhookAttemptsPerMinutePerTenant;

    public async Task RegisterWebhookAttemptAsync(Guid tenantId, CancellationToken ct = default) =>
        await IncrementAsync(WebhookKey(tenantId), ct);

    public async Task<bool> IsAdminActionThrottledAsync(Guid tenantId, CancellationToken ct = default) =>
        await cache.GetAsync<int?>(AdminActionKey(tenantId), ct) >= MaxAdminActionAttemptsPerMinutePerTenant;

    public async Task RegisterAdminActionAttemptAsync(Guid tenantId, CancellationToken ct = default) =>
        await IncrementAsync(AdminActionKey(tenantId), ct);

    private async Task IncrementAsync(string key, CancellationToken ct)
    {
        var count = await cache.GetAsync<int?>(key, ct) ?? 0;
        await cache.SetAsync(key, count + 1, Window, ct);
    }

    private static string WebhookKey(Guid tenantId) => $"paymentapp:throttle:{tenantId:N}:webhook";

    private static string AdminActionKey(Guid tenantId) => $"paymentapp:throttle:{tenantId:N}:admin-action";
}
