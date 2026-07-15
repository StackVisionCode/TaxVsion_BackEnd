using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.TenantDomains;

namespace TaxVision.Auth.Infrastructure.Persistence.Repositories;

public sealed class TenantDomainRepository(AuthDbContext db) : ITenantDomainRepository
{
    public Task<TenantDomain?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.TenantDomains.FirstOrDefaultAsync(domain => domain.Id == id, ct);

    public Task<TenantDomain?> GetByHostAsync(string host, CancellationToken ct = default) =>
        db.TenantDomains.FirstOrDefaultAsync(domain => domain.Host == host, ct);

    public async Task<IReadOnlyList<TenantDomain>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        await db
            .TenantDomains.Where(domain => domain.TenantId == tenantId)
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

    public async Task<IReadOnlyList<TenantDomain>> GetProvisioningCustomHostnamesAsync(
        CancellationToken ct = default
    ) =>
        await db
            .TenantDomains.Where(domain =>
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
