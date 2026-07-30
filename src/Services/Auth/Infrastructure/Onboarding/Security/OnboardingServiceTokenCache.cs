using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Infrastructure.Onboarding.Security;

/// <summary>
/// PayFlow (auditoría F13) — cachea los tokens de servicio M2M (<c>GenerateScopedServiceToken</c>)
/// que los ~7 HttpClients de Onboarding mintaban en CADA llamada saliente. Cada cliente llama
/// siempre con los mismos <c>tenantId</c>/<c>permissions</c>/<c>scopes</c>/<c>audience</c>/<c>lifetimeMinutes</c>
/// (constantes propias del cliente), así que <c>clientId</c> alcanza como clave de cache.
/// <para>
/// Singleton: resuelve <see cref="IJwtTokenGenerator"/> (Scoped, pero stateless — sólo depende de
/// <c>JwtOptions</c> y <see cref="TaxVision.Auth.Infrastructure.Security.SigningKeyProvider"/>, ambos
/// singleton-safe) en un scope efímero por cada mint, para no romper su lifetime registrado.
/// </para>
/// <para>
/// Refresca con margen de seguridad antes de la expiración real, para que un request en vuelo
/// nunca reciba un token a punto de vencer. Una carrera entre dos llamadas concurrentes que
/// encuentran la cache vacía puede mintear el token dos veces — aceptable: mintear es barato y sin
/// efectos secundarios, y el objetivo de esta cache es eliminar el mint-por-llamada del caso común,
/// no garantizar exactamente-una-mint bajo concurrencia.
/// </para>
/// <para>
/// Auditoría F27 — la entrada cacheada guarda también los parámetros con los que se minteó
/// (<see cref="CacheEntry"/>), no solo el token. Si un caller reusara un <c>clientId</c> existente
/// con otro <c>audience</c>/<c>scopes</c>/etc. (hoy no pasa — cada uno de los 7 clientes M2M usa
/// parámetros fijos por <c>clientId</c>, ver doc-comments de cada <c>HttpClient</c>), un mismatch se
/// trata como cache-miss y remintea, en vez de devolver en silencio el token cacheado equivocado.
/// </para>
/// </summary>
public sealed class OnboardingServiceTokenCache(IServiceScopeFactory scopeFactory)
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(15);

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    private sealed record CacheEntry(
        Guid TenantId,
        IReadOnlyCollection<string> Permissions,
        IReadOnlyCollection<string> Scopes,
        string Audience,
        int LifetimeMinutes,
        AccessToken Token,
        DateTime ExpiresAtUtc
    );

    public AccessToken GetOrCreate(
        Guid tenantId,
        string clientId,
        IReadOnlyCollection<string> permissions,
        IReadOnlyCollection<string> scopes,
        string audience,
        int lifetimeMinutes
    )
    {
        if (
            _cache.TryGetValue(clientId, out var cached)
            && cached.ExpiresAtUtc > DateTime.UtcNow + RefreshMargin
            && Matches(cached, tenantId, permissions, scopes, audience, lifetimeMinutes)
        )
            return cached.Token;

        using var scope = scopeFactory.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();
        var token = tokens.GenerateScopedServiceToken(
            tenantId,
            clientId,
            permissions,
            scopes,
            audience,
            lifetimeMinutes
        );

        _cache[clientId] = new CacheEntry(
            tenantId,
            permissions,
            scopes,
            audience,
            lifetimeMinutes,
            token,
            DateTime.UtcNow.AddSeconds(token.ExpiresInSeconds)
        );
        return token;
    }

    private static bool Matches(
        CacheEntry cached,
        Guid tenantId,
        IReadOnlyCollection<string> permissions,
        IReadOnlyCollection<string> scopes,
        string audience,
        int lifetimeMinutes
    ) =>
        cached.TenantId == tenantId
        && cached.Audience == audience
        && cached.LifetimeMinutes == lifetimeMinutes
        && cached.Permissions.SequenceEqual(permissions)
        && cached.Scopes.SequenceEqual(scopes);
}
