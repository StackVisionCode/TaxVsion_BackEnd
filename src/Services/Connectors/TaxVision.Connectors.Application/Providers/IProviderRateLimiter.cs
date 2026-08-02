using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Application.Providers;

/// <summary>
/// Rate limiter por provider (Redis-backed en producción, para que N réplicas compartan el mismo
/// presupuesto). Tres mecanismos: (1) ventana fija global de requests/segundo — protege al
/// provider externo (Gmail/Graph), compartida entre TODOS los tenants; (2) ventana fija
/// per-tenant — protege el fair-share de cada tenant sobre el cupo global, evita que uno solo
/// (Rate Limit Fase 0.3) consuma todo el presupuesto y apague al resto; ambas deben pasar, la
/// más estricta manda; (3) cooldown explícito cuando el provider ya devolvió 429 — otros callers
/// lo respetan sin tener que descubrirlo ellos mismos.
/// </summary>
public interface IProviderRateLimiter
{
    /// <summary>Espera hasta que sea seguro hacer 1 request al provider en nombre de <paramref name="tenantId"/> (ventana global + ventana per-tenant + cooldown activo, si hay).</summary>
    Task WaitForSlotAsync(ProviderCode providerCode, Guid tenantId, CancellationToken ct = default);

    /// <summary>Registra un 429 reciente — activa el cooldown compartido por <paramref name="retryAfter"/>.</summary>
    Task RecordRateLimitedAsync(ProviderCode providerCode, TimeSpan retryAfter, CancellationToken ct = default);
}
