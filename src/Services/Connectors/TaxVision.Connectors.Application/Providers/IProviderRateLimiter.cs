using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Application.Providers;

/// <summary>
/// Rate limiter por provider (Redis-backed en producción, para que N réplicas compartan el mismo
/// presupuesto). Dos mecanismos: (1) ventana fija de requests/segundo, proactivo; (2) cooldown
/// explícito cuando el provider ya devolvió 429 — otros callers lo respetan sin tener que
/// descubrirlo ellos mismos.
/// </summary>
public interface IProviderRateLimiter
{
    /// <summary>Espera hasta que sea seguro hacer 1 request al provider (ventana + cooldown activo, si hay).</summary>
    Task WaitForSlotAsync(ProviderCode providerCode, CancellationToken ct = default);

    /// <summary>Registra un 429 reciente — activa el cooldown compartido por <paramref name="retryAfter"/>.</summary>
    Task RecordRateLimitedAsync(ProviderCode providerCode, TimeSpan retryAfter, CancellationToken ct = default);
}
