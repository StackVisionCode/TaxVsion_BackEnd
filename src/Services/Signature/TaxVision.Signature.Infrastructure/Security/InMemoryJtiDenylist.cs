using System.Collections.Concurrent;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Infrastructure.Security;

/// <summary>
/// Denylist in-memory con TTL. Sirve para dev/single-node — se sustituye por una
/// implementación Redis en producción vía DI. La limpieza de entradas expiradas es
/// oportunística (pasa por ellas al consultar).
/// </summary>
public sealed class InMemoryJtiDenylist : IJtiDenylist
{
    private readonly ConcurrentDictionary<string, DateTime> _revoked = new();

    public Task<bool> IsRevokedAsync(string jti, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(jti))
            return Task.FromResult(false);

        if (_revoked.TryGetValue(jti, out var expiresAt))
        {
            if (expiresAt > DateTime.UtcNow)
                return Task.FromResult(true);
            _revoked.TryRemove(jti, out _);
        }
        return Task.FromResult(false);
    }

    public Task RevokeAsync(string jti, DateTime expiresAtUtc, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(jti) || expiresAtUtc <= DateTime.UtcNow)
            return Task.CompletedTask;
        _revoked[jti] = expiresAtUtc;
        return Task.CompletedTask;
    }
}
