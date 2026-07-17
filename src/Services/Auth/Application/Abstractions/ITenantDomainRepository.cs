using TaxVision.Auth.Domain.TenantDomains;

namespace TaxVision.Auth.Application.Abstractions;

public interface ITenantDomainRepository
{
    Task<TenantDomain?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Resuelve un Host completo (subdominio o custom hostname) a su TenantDomain — Fase A3.</summary>
    Task<TenantDomain?> GetByHostAsync(string host, CancellationToken ct = default);

    Task<IReadOnlyList<TenantDomain>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Todo host en estado Active — la allowlist que valida el middleware de resolución (Fase A3).</summary>
    Task<IReadOnlyList<string>> GetActiveHostsAsync(CancellationToken ct = default);

    Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default);

    Task<bool> HostExistsAsync(string host, CancellationToken ct = default);

    Task AddAsync(TenantDomain domain, CancellationToken ct = default);

    /// <summary>Custom hostnames en Provisioning — lo que el poller de Cloudflare re-consulta (Fase A5).</summary>
    Task<IReadOnlyList<TenantDomain>> GetProvisioningCustomHostnamesAsync(CancellationToken ct = default);
}

public interface ITenantSubdomainReservationRepository
{
    Task<TenantSubdomainReservation?> GetActiveBySlugAsync(
        string slug,
        DateTime nowUtc,
        CancellationToken ct = default
    );

    Task AddAsync(TenantSubdomainReservation reservation, CancellationToken ct = default);
}
