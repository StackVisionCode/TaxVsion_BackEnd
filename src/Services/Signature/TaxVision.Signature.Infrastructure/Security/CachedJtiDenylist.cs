using BuildingBlocks.Caching;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Infrastructure.Security;

/// <summary>
/// Denylist de <c>jti</c> respaldada por <see cref="ICacheService"/> (Redis en producción, memoria
/// como fallback en dev) — el mismo store distribuido que usa el resto de la flota, así que una
/// revocación vale para todos los nodos, no solo el que la escribió.
///
/// <para>
/// Fail-open deliberado: si el store no responde, <see cref="IsRevokedAsync"/> devuelve <c>false</c>
/// (no se bloquea la firma por un hipo del cache; el <c>RevocationEpoch</c> sigue cubriendo la
/// revocación global). La escritura es best-effort por la misma razón.
/// </para>
/// </summary>
public sealed class CachedJtiDenylist(ICacheService cache, ILogger<CachedJtiDenylist> logger) : IJtiDenylist
{
    private static string Key(string jti) => $"signature:jti:denylist:{jti}";

    public async Task<bool> IsRevokedAsync(string jti, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(jti))
            return false;

        try
        {
            return await cache.GetAsync<bool?>(Key(jti), ct) == true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "JTI denylist lookup failed; failing open (treating token as not revoked).");
            return false;
        }
    }

    public async Task RevokeAsync(string jti, DateTime expiresAtUtc, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(jti))
            return;

        var ttl = expiresAtUtc - DateTime.UtcNow;
        if (ttl <= TimeSpan.Zero)
            return;

        try
        {
            await cache.SetAsync(Key(jti), true, ttl, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "JTI denylist revoke failed for a token; the old link may remain usable until expiry."
            );
        }
    }
}
