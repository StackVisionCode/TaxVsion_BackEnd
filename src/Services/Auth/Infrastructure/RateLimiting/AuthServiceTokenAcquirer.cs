using TaxVision.Auth.Infrastructure.Onboarding.Security;

namespace TaxVision.Auth.Infrastructure.RateLimiting;

/// <summary>
/// RateLimit Fase 2 — adaptador de <see cref="BuildingBlocks.Infrastructure.Security.IServiceTokenAcquirer"/>
/// (contrato compartido que <c>HttpPlanRateLimitReader</c> consume) para Auth.
/// <para>
/// A diferencia del resto de los servicios (Tenant/Customer/Scribe/Signature/etc.), Auth es la
/// fuente misma de los tokens M2M — no tiene un acquirer HTTP-hacia-Auth propio porque eso sería
/// llamarse a sí mismo por HTTP para algo que puede resolver en proceso. Auth ya mintea tokens de
/// servicio localmente para sus propios clientes salientes (Subscription/PaymentApp/Tenant/
/// Documents/CloudStorage) vía <see cref="OnboardingServiceTokenCache"/> (que a su vez llama a
/// <c>IJwtTokenGenerator.GenerateScopedServiceToken</c> directo, sin pasar por el endpoint HTTP
/// <c>POST auth/service-token</c>) — este adaptador reusa exactamente ese mecanismo en vez de
/// inventar uno nuevo.
/// </para>
/// <para>
/// Mismos parámetros que <c>SubscriptionActivationClient</c>/<c>PlanCatalogClient</c> (permissions
/// y scopes vacíos, audience "TaxVision.Services"): el endpoint que consume este token
/// (<c>GET subscriptions/internal/plan-rate-limits</c>) usa una policy ServiceOnly que solo exige
/// <c>actor_type=Service</c>, sin scopes de permiso.
/// </para>
/// </summary>
internal sealed class AuthServiceTokenAcquirer(OnboardingServiceTokenCache tokenCache)
    : BuildingBlocks.Infrastructure.Security.IServiceTokenAcquirer
{
    private const string ClientId = "auth-ratelimit-plan-catalog";
    private const string Audience = "TaxVision.Services";
    private const int LifetimeMinutes = 5;

    public async Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default)
    {
        var token = await tokenCache.GetOrCreateAsync(
            tenantId,
            ClientId,
            permissions: [],
            scopes: [],
            audience: Audience,
            lifetimeMinutes: LifetimeMinutes,
            ct
        );
        return token.Token;
    }
}
