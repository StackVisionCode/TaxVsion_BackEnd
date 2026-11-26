using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Sessions;
using TaxVision.Auth.Infrastructure.Persistence;

namespace TaxVision.Auth.Infrastructure.Security;

/// <summary>
/// Aplica la política de sesión en cada refresh. La política se lee de la base como mucho una vez por minuto
/// (cambia muy poco y el refresh es frecuente); al guardarla, <see cref="Invalidate"/> la descarta en esta
/// instancia. Con varias réplicas, las demás la ven en menos de un minuto.
/// </summary>
public sealed class SessionLifetimeGate(IServiceScopeFactory scopes, IOptions<JwtOptions> jwt) : ISessionLifetimeGate
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);

    private sealed record Cached(PlatformSessionPolicy Policy, DateTime ExpiresAtUtc);

    private Cached? _cached;

    public async Task<string?> EndReasonAsync(UserSession session, CancellationToken ct = default)
    {
        var nowUtc = DateTime.UtcNow;
        var policy = await CurrentAsync(nowUtc, ct);
        // El front renueva una vez por vida de token mientras hay actividad: ese es el margen de la inactividad.
        return policy.EndReason(session, TimeSpan.FromMinutes(jwt.Value.AccessMinutes), nowUtc);
    }

    public async Task<SessionActivityWindow> ActivityWindowAsync(CancellationToken ct = default)
    {
        var nowUtc = DateTime.UtcNow;
        var policy = await CurrentAsync(nowUtc, ct);
        return policy.ActivityWindow(TimeSpan.FromMinutes(jwt.Value.AccessMinutes), nowUtc);
    }

    public void Invalidate() => Volatile.Write(ref _cached, null);

    private async Task<PlatformSessionPolicy> CurrentAsync(DateTime nowUtc, CancellationToken ct)
    {
        if (Volatile.Read(ref _cached) is { } cached && cached.ExpiresAtUtc > nowUtc)
            return cached.Policy;

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var policy =
            await db
                .PlatformSessionPolicies.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == PlatformSessionPolicy.GlobalId, ct)
            ?? PlatformSessionPolicy.CreateDefault(nowUtc);
        Volatile.Write(ref _cached, new Cached(policy, nowUtc + CacheFor));
        return policy;
    }
}
