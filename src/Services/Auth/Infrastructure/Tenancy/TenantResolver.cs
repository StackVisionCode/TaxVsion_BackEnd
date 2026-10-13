using BuildingBlocks.Platform;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.TenantDomains;

namespace TaxVision.Auth.Infrastructure.Tenancy;

/// <summary>
/// Host → tenant. El dominio de la plataforma sale de <see cref="IPlatformSettings"/>, no del host
/// guardado en <c>TenantDomains</c>:
/// <list type="bullet">
///   <item>El host de la API es el del tenant Platform (login del PlatformAdmin).</item>
///   <item>Un host de oficina (<c>{slug}.{dominio}</c>) se busca por slug.</item>
///   <item>Cualquier otro es un dominio propio de la oficina y se busca por host exacto.</item>
/// </list>
/// Así, cambiar el dominio de la plataforma no deja a ninguna oficina sin resolver.
/// </summary>
public sealed class TenantResolver(
    ITenantDomainRepository domains,
    ITenantRegistry tenants,
    ITenantResolutionCache cache,
    IPlatformSettings platform
) : ITenantResolver
{
    public async Task<HostResolutionResult> ResolveAsync(string? host, CancellationToken ct = default)
    {
        var normalizedHost = PlatformSettingsSnapshot.NormalizeHost(host);
        if (string.IsNullOrEmpty(normalizedHost))
            return HostResolutionResult.Unresolved(TenantResolutionFailureReason.HostMissing);

        if (await cache.TryGetAsync(normalizedHost, ct) is { } cachedTenantId)
            return HostResolutionResult.Resolved(cachedTenantId);

        var settings = platform.Current;
        Guid tenantId;
        if (normalizedHost == PlatformSettingsSnapshot.NormalizeHost(settings.ApiHost))
        {
            tenantId = PlatformTenant.Id;
        }
        else
        {
            var domain = settings.TryGetTenantSlug(normalizedHost, out var slug)
                ? await domains.GetSubdomainBySlugAsync(slug, ct)
                : await GetCustomHostnameAsync(normalizedHost, ct);
            if (domain is null || domain.Status != TenantDomainStatus.Active)
                return HostResolutionResult.Unresolved(TenantResolutionFailureReason.HostUnknown);

            tenantId = domain.TenantId;
        }

        var tenant = await tenants.GetByIdAsync(tenantId, ct);
        if (tenant is null || !tenant.IsActive)
            return HostResolutionResult.Unresolved(TenantResolutionFailureReason.TenantInactive);

        await cache.SetAsync(normalizedHost, tenantId, ct);
        return HostResolutionResult.Resolved(tenantId);
    }

    // Por host exacto solo se aceptan dominios propios: un subdominio de plataforma con el dominio
    // anterior guardado no debe seguir resolviendo.
    private async Task<TenantDomain?> GetCustomHostnameAsync(string host, CancellationToken ct)
    {
        var domain = await domains.GetByHostAsync(host, ct);
        return domain?.DomainType == TenantDomainType.CustomHostname ? domain : null;
    }
}
