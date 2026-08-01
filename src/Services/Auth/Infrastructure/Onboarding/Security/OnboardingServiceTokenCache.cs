using BuildingBlocks.Security;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Infrastructure.Onboarding.Security;

/// <summary>
/// PayFlow (auditoría F13, F25) — cachea los tokens de servicio M2M (<c>GenerateScopedServiceToken</c>)
/// que los HttpClients de Onboarding mintaban en CADA llamada saliente, componiendo
/// <see cref="ExpiringValueCache{TKey,TValue}"/> (F25) en vez de un <c>ConcurrentDictionary</c> +
/// <c>Matches(...)</c> hecho a mano.
/// <para>
/// Resuelve <see cref="IJwtTokenGenerator"/> (Scoped, pero stateless — sólo depende de
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
/// </summary>
public sealed class OnboardingServiceTokenCache(IServiceScopeFactory scopeFactory)
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(15);

    private readonly ExpiringValueCache<ServiceTokenCacheKey, AccessToken> _cache = new(RefreshMargin);

    public Task<AccessToken> GetOrCreateAsync(
        Guid tenantId,
        string clientId,
        IReadOnlyCollection<string> permissions,
        IReadOnlyCollection<string> scopes,
        string audience,
        int lifetimeMinutes,
        CancellationToken ct = default
    )
    {
        var key = ServiceTokenCacheKey.Create(tenantId, clientId, permissions, scopes, audience, lifetimeMinutes);
        return _cache.GetOrCreateAsync(
            key,
            _ => MintAsync(tenantId, clientId, permissions, scopes, audience, lifetimeMinutes),
            ct
        );
    }

    private Task<(AccessToken Value, DateTime ExpiresAtUtc)> MintAsync(
        Guid tenantId,
        string clientId,
        IReadOnlyCollection<string> permissions,
        IReadOnlyCollection<string> scopes,
        string audience,
        int lifetimeMinutes
    )
    {
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
        return Task.FromResult((token, DateTime.UtcNow.AddSeconds(token.ExpiresInSeconds)));
    }
}
