using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.TenantDomains;

namespace TaxVision.Auth.Infrastructure.Persistence.Repositories;

public sealed class TenantDomainRepository(AuthDbContext db) : ITenantDomainRepository
{
    // IgnoreQueryFilters(): mismo bug que UserRepository.GetByIdAsync (ver su comentario) — el
    // único llamador (TenantDomainAccessGuard.LoadOwnedAsync) ya valida
    // domain.TenantId != tenantId post-fetch, así que el filtro ambiental era redundante.
    public Task<TenantDomain?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.TenantDomains.IgnoreQueryFilters().FirstOrDefaultAsync(domain => domain.Id == id, ct);

    // IgnoreQueryFilters(): mismo bug, más crítico todavía — este es el método que usa
    // TenantResolver.ResolveAsync en CADA login/request entrante para averiguar de qué tenant
    // es el host. Se ejecuta ANTES de que exista ningún TenantContext (es justamente lo que
    // este método existe para determinar) — con el filtro global puesto, el predicado implícito
    // "TenantId = @ef_filter__EffectiveTenantId" nunca matchea nada (no hay tenant ambiental
    // todavía) y la resolución de host falla siempre, tirando 404 en /auth/login para
    // absolutamente cualquier subdominio o custom hostname.
    public Task<TenantDomain?> GetByHostAsync(string host, CancellationToken ct = default) =>
        db.TenantDomains.IgnoreQueryFilters().FirstOrDefaultAsync(domain => domain.Host == host, ct);

    public async Task<IReadOnlyList<TenantDomain>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        await db
            .TenantDomains.IgnoreQueryFilters()
            .Where(domain => domain.TenantId == tenantId)
            .OrderBy(domain => domain.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<string>> GetActiveHostsAsync(CancellationToken ct = default) =>
        await db
            .TenantDomains.Where(domain => domain.Status == TenantDomainStatus.Active)
            .Select(domain => domain.Host)
            .ToListAsync(ct);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default) =>
        db.TenantDomains.AnyAsync(domain => domain.SubdomainSlug == slug, ct);

    public Task<bool> HostExistsAsync(string host, CancellationToken ct = default) =>
        db.TenantDomains.AnyAsync(domain => domain.Host == host, ct);

    public async Task AddAsync(TenantDomain domain, CancellationToken ct = default) =>
        await db.TenantDomains.AddAsync(domain, ct);

    // IgnoreQueryFilters(): barrido cross-tenant deliberado desde un BackgroundService
    // (TenantDomainProvisioningPoller) que nunca tiene JWT/ITenantContext poblado — sin esto,
    // el poller de aprovisionamiento de Cloudflare quedaba silenciosamente sin efecto (0
    // hostnames encontrados siempre), igual que los demás jobs de Fase 5.2 ya corregidos.
    public async Task<IReadOnlyList<TenantDomain>> GetProvisioningCustomHostnamesAsync(
        CancellationToken ct = default
    ) =>
        await db
            .TenantDomains.IgnoreQueryFilters()
            .Where(domain =>
                domain.DomainType == TenantDomainType.CustomHostname && domain.Status == TenantDomainStatus.Provisioning
            )
            .ToListAsync(ct);
}

public sealed class TenantSubdomainReservationRepository(AuthDbContext db) : ITenantSubdomainReservationRepository
{
    public Task<TenantSubdomainReservation?> GetActiveBySlugAsync(
        string slug,
        DateTime nowUtc,
        CancellationToken ct = default
    ) =>
        db.TenantSubdomainReservations.FirstOrDefaultAsync(
            reservation =>
                reservation.SubdomainSlug == slug
                && reservation.ConsumedAtUtc == null
                && reservation.ExpiresAtUtc > nowUtc,
            ct
        );

    public async Task AddAsync(TenantSubdomainReservation reservation, CancellationToken ct = default) =>
        await db.TenantSubdomainReservations.AddAsync(reservation, ct);
}
