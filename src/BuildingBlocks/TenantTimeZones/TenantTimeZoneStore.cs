using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.TenantTimeZones;

/// <summary>
/// IgnoreQueryFilters + tenantId explícito: corre en consumers y jobs sin tenant ambiental. Toma el DbContext
/// base y usa Set&lt;&gt;, así no depende del DbContext concreto del servicio.
/// </summary>
public sealed class TenantTimeZoneStore(DbContext db) : ITenantTimeZoneStore
{
    public Task<TenantTimeZoneProjection?> FindAsync(Guid tenantId, CancellationToken ct = default) =>
        db.Set<TenantTimeZoneProjection>().IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

    public Task<string?> GetTimeZoneIdAsync(Guid tenantId, CancellationToken ct = default) =>
        db.Set<TenantTimeZoneProjection>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .Select(p => p.TimeZoneId)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(TenantTimeZoneProjection projection, CancellationToken ct = default) =>
        await db.Set<TenantTimeZoneProjection>().AddAsync(projection, ct);
}
