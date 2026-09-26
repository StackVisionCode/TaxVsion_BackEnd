using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.RateLimiting;

namespace BuildingBlocks.Infrastructure.RateLimiting;

/// <summary>
/// Evalúa una clave de rate-limit según el <see cref="RateLimitAlgorithm"/> declarado por la
/// política — cierra el hallazgo de la auditoría post-Fase-9 (#8): antes de esto,
/// <see cref="TieredRateLimitEvaluator"/> siempre contaba con <c>IRateCounter.IncrementAndGetAsync</c>
/// (ventana fija) sin importar qué algoritmo declarara la política. Interfaz separada de
/// <see cref="IRateCounter"/> a propósito — ese sigue siendo el primitivo simple de incremento
/// atómico usado por 7+ consumidores no relacionados con políticas de catálogo (login throttler,
/// limiters de proveedor de Connectors/Postmaster, etc.), que siempre quieren ventana fija y no
/// deberían absorber la complejidad de un algoritmo configurable que no necesitan.
/// </summary>
public interface IRateLimitAlgorithmCounter
{
    /// <summary>
    /// Consume un permiso si hay cupo dentro de <paramref name="window"/>. Un rechazo no consume cupo, y
    /// trae cuánto falta para que se libere el próximo permiso.
    /// </summary>
    Task<RateLimitCounterResult> EvaluateAsync(
        RateCounterKey key,
        RateLimitAlgorithm algorithm,
        int limit,
        TimeSpan window,
        CancellationToken ct = default
    );
}

/// <param name="Exceeded">No había cupo: la evaluación se rechazó sin consumir.</param>
/// <param name="RetryAfter">Tiempo hasta que se libere un permiso; <see cref="TimeSpan.Zero"/> si se permitió.</param>
public readonly record struct RateLimitCounterResult(bool Exceeded, TimeSpan RetryAfter)
{
    public static RateLimitCounterResult Allowed => new(false, TimeSpan.Zero);

    public static RateLimitCounterResult Rejected(TimeSpan retryAfter) => new(true, retryAfter);
}
