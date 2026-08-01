using BuildingBlocks.Infrastructure.RateLimit;
using StackExchange.Redis;
using TaxVision.PaymentApp.Application.Abstractions;

namespace TaxVision.PaymentApp.Infrastructure.Security;

/// <summary>
/// Implementación de <see cref="IPaymentAttemptThrottle"/> respaldada por Redis — mismo patrón de
/// defensa en profundidad que <c>Auth.Infrastructure.Security.LoginThrottler</c> (no es el rate
/// limit primario). F26: el incremento del contador ahora es atómico vía <see cref="IRateCounter"/>
/// (antes: GET+SET no atómico sobre <c>ICacheService</c>, con lost-updates reales bajo concurrencia).
/// Dos cambios de comportamiento derivados de esto, ambos aceptados:
/// <list type="bullet">
/// <item>La ventana pasa de deslizante (cada intento reseteaba el TTL a 1 minuto completo) a fija
/// (el TTL se fija solo en el primer incremento del ciclo) — mismo criterio que ya usan los 5
/// rate limiters Family A (Connectors/Postmaster).</item>
/// <item>El check-then-register entre <c>IsXThrottledAsync</c> y <c>RegisterXAttemptAsync</c> sigue
/// siendo un TOCTOU no atómico — limitación pre-existente conocida, igual que se dejó
/// <c>ILoginThrottler</c> en F08; solo el incremento en sí se volvió atómico.</item>
/// </list>
/// La lectura del contador (<c>IsXThrottledAsync</c>) usa <see cref="IConnectionMultiplexer"/>
/// directo en vez de <c>ICacheService</c>: <see cref="IRateCounter"/> escribe un string Redis crudo
/// vía <c>INCR</c>, formato incompatible con el hash que <c>IDistributedCache</c>/StackExchangeRedis
/// espera para sus propias claves.
/// </summary>
public sealed class PaymentAttemptThrottle(IConnectionMultiplexer redis, IRateCounter rateCounter)
    : IPaymentAttemptThrottle
{
    private const int MaxWebhookAttemptsPerMinutePerTenant = 60;
    private const int MaxAdminActionAttemptsPerMinutePerTenant = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public async Task<bool> IsWebhookThrottledAsync(Guid tenantId, CancellationToken ct = default) =>
        await GetCountAsync(WebhookKey(tenantId)) >= MaxWebhookAttemptsPerMinutePerTenant;

    public Task RegisterWebhookAttemptAsync(Guid tenantId, CancellationToken ct = default) =>
        rateCounter.IncrementAndGetAsync(WebhookKey(tenantId), Window, ct);

    public async Task<bool> IsAdminActionThrottledAsync(Guid tenantId, CancellationToken ct = default) =>
        await GetCountAsync(AdminActionKey(tenantId)) >= MaxAdminActionAttemptsPerMinutePerTenant;

    public Task RegisterAdminActionAttemptAsync(Guid tenantId, CancellationToken ct = default) =>
        rateCounter.IncrementAndGetAsync(AdminActionKey(tenantId), Window, ct);

    private async Task<long> GetCountAsync(RateCounterKey key) =>
        (long)await redis.GetDatabase().StringGetAsync(key.Value);

    private static RateCounterKey WebhookKey(Guid tenantId) =>
        RateCounterKey.From($"paymentapp:throttle:{tenantId:N}:webhook");

    private static RateCounterKey AdminActionKey(Guid tenantId) =>
        RateCounterKey.From($"paymentapp:throttle:{tenantId:N}:admin-action");
}
