using StackExchange.Redis;

namespace TaxVision.Scribe.Infrastructure.Rendering;

public sealed class RedisTemplateSourceCache(IConnectionMultiplexer redis) : ITemplateSourceCache
{
    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        var value = await redis.GetDatabase().StringGetAsync(key);
        return value.IsNullOrEmpty ? null : value.ToString();
    }

    // TTL indefinido a propósito: la invalidación es explícita al publicar una nueva versión (Fase 6),
    // no por expiración — el plan pide "TTL indefinido" para el AST cache.
    public Task SetAsync(string key, string value, CancellationToken ct = default) =>
        redis.GetDatabase().StringSetAsync(key, value);
}
