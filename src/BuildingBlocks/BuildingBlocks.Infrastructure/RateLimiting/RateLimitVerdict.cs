namespace BuildingBlocks.Infrastructure.RateLimiting;

/// <summary>
/// Resultado de <see cref="ITieredRateLimitEvaluator.EvaluateAsync"/> — qué capa (si alguna)
/// disparó, y los datos que el filtro de Fase 3 necesita para los headers §6.3
/// (<c>X-RateLimit-Layer</c>/<c>-Limit</c>/<c>Retry-After</c>).
/// </summary>
public sealed record RateLimitVerdict(bool IsExceeded, string? Layer, int Limit, int RetryAfterSeconds)
{
    public static RateLimitVerdict Allowed() => new(false, null, 0, 0);

    public static RateLimitVerdict Exceeded(string layer, int limit, int retryAfterSeconds) =>
        new(true, layer, limit, retryAfterSeconds);
}
