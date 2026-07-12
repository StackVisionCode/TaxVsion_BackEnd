using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Requests.Queries.List;

namespace TaxVision.Signature.Infrastructure.Persistence.Queries;

/// <summary>
/// Decorator con caché distribuido (30s TTL) sobre el read service base de solicitudes.
/// La clave incluye tenant + filtros + paginación — invalidación por TTL es suficiente
/// para el dashboard (delay aceptable). Al cambiar significativamente el modelo de
/// respuesta se debe bumpear la <see cref="CacheKeyVersion"/>.
/// </summary>
public sealed class CachedSignatureRequestReadService(ISignatureRequestReadService inner, IDistributedCache cache)
    : ISignatureRequestReadService
{
    private const string CacheKeyVersion = "v1";
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private static readonly DistributedCacheEntryOptions Options = new() { AbsoluteExpirationRelativeToNow = Ttl };

    public async Task<ListSignatureRequestsResult> ListAsync(
        ListSignatureRequestsQuery query,
        CancellationToken ct = default
    )
    {
        var key = BuildCacheKey(query);
        var cached = await cache.GetAsync(key, ct);
        if (cached is not null)
        {
            var deserialized = JsonSerializer.Deserialize<ListSignatureRequestsResult>(cached);
            if (deserialized is not null)
                return deserialized;
        }

        var fresh = await inner.ListAsync(query, ct);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(fresh);
        await cache.SetAsync(key, bytes, Options, ct);
        return fresh;
    }

    private static string BuildCacheKey(ListSignatureRequestsQuery q) =>
        $"sig:list:{CacheKeyVersion}:{q.TenantId:N}:s={q.Status}:c={q.Category}:p={q.Page}:z={q.PageSize}";
}
