using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Requests.Queries.List;

namespace TaxVision.Signature.Infrastructure.Persistence.Queries;

/// <summary>
/// Decorator con caché distribuido sobre el read service base de solicitudes. La clave incluye una
/// VERSIÓN por tenant: al cambiar una solicitud (crear/enviar/firmar/rechazar/…) se bumpea esa versión
/// (<see cref="InvalidateAsync"/>) y todas las páginas/filtros cacheados del tenant quedan obsoletos al
/// instante (cache-miss → fetch fresco). Así el dashboard se actualiza en el acto (realtime + refresh)
/// sin esperar al TTL, que queda como red de seguridad. Al cambiar el modelo de respuesta se bumpea
/// <see cref="CacheKeyVersion"/>.
/// </summary>
public sealed class CachedSignatureRequestReadService(ISignatureRequestReadService inner, IDistributedCache cache)
    : ISignatureRequestReadService,
        ISignatureRequestListCacheInvalidator
{
    private const string CacheKeyVersion = "v4";
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan VersionTtl = TimeSpan.FromDays(30);
    private static readonly DistributedCacheEntryOptions Options = new() { AbsoluteExpirationRelativeToNow = Ttl };
    private static readonly DistributedCacheEntryOptions VersionOptions = new()
    {
        AbsoluteExpirationRelativeToNow = VersionTtl,
    };

    public async Task<ListSignatureRequestsResult> ListAsync(
        ListSignatureRequestsQuery query,
        CancellationToken ct = default
    )
    {
        var version = await GetTenantVersionAsync(query.TenantId, ct);
        var key = BuildCacheKey(query, version);
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

    /// <summary>Bumpea la versión de lista del tenant → invalida TODAS sus páginas/filtros cacheados.</summary>
    public async Task InvalidateAsync(Guid tenantId, CancellationToken ct = default)
    {
        var current = await GetTenantVersionAsync(tenantId, ct);
        var next = (current + 1).ToString();
        await cache.SetAsync(VersionKey(tenantId), System.Text.Encoding.UTF8.GetBytes(next), VersionOptions, ct);
    }

    private async Task<long> GetTenantVersionAsync(Guid tenantId, CancellationToken ct)
    {
        var raw = await cache.GetAsync(VersionKey(tenantId), ct);
        if (raw is null)
            return 0;
        return long.TryParse(System.Text.Encoding.UTF8.GetString(raw), out var v) ? v : 0;
    }

    private static string VersionKey(Guid tenantId) => $"sig:list:ver:{CacheKeyVersion}:{tenantId:N}";

    // `e` (editableOnly) es parte de la clave: Draft y All comparten Status=null y colisionarían sin él.
    // `va`/`u`: la visibilidad por asignación (P2) hace que el resultado dependa del actor → la clave DEBE
    // variar por usuario, o dos usuarios compartirían páginas cacheadas (fuga de visibilidad). Los que ven
    // todo (view_all/admin) comparten una sola entrada (u=Empty).
    private static string BuildCacheKey(ListSignatureRequestsQuery q, long version) =>
        $"sig:list:{CacheKeyVersion}:{q.TenantId:N}:g={version}:va={q.CanViewAll}:u={(q.CanViewAll ? Guid.Empty : q.ActorUserId):N}:s={q.Status}:c={q.Category}:e={q.EditableOnly}:p={q.Page}:z={q.PageSize}";
}
