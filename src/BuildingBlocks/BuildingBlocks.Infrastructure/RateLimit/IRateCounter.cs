namespace BuildingBlocks.Infrastructure.RateLimit;

/// <summary>
/// Contador de rate-limiting atómico compartido entre réplicas. La implementación garantiza que
/// el incremento y la fijación del TTL de la ventana ocurren en una sola operación indivisible —
/// a diferencia de <c>IDatabase.StringIncrementAsync</c> + <c>KeyExpireAsync</c> por separado, que
/// deja una clave sin TTL si el proceso muere entre ambas llamadas.
/// </summary>
public interface IRateCounter
{
    /// <summary>Incrementa el contador de <paramref name="key"/> y devuelve el nuevo valor. Si es
    /// el primer incremento de la clave, fija su expiración a <paramref name="window"/>.</summary>
    Task<long> IncrementAndGetAsync(RateCounterKey key, TimeSpan window, CancellationToken ct = default);
}
