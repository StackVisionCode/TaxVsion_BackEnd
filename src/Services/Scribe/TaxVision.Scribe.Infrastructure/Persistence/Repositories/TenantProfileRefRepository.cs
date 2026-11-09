using Microsoft.EntityFrameworkCore;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Infrastructure.Persistence.Repositories;

/// <summary>
/// IgnoreQueryFilters por el mismo motivo que <see cref="TenantLogoRefRepository"/>: el render corre
/// con token M2M (ActorType.Service, sin claim tenant_id), asi que el aislamiento lo da el parametro
/// explicito, no el filtro global.
/// </summary>
public sealed class TenantProfileRefRepository(ScribeDbContext dbContext) : ITenantProfileRefRepository
{
    public Task<TenantProfileRef?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        dbContext.TenantProfileRefs.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.TenantId == tenantId, ct);

    public async Task AddIfMissingAsync(
        Guid tenantId,
        string name,
        string subDomain,
        DateTime nowUtc,
        CancellationToken ct = default
    )
    {
        if (await GetByTenantIdAsync(tenantId, ct) is null)
            await dbContext.TenantProfileRefs.AddAsync(TenantProfileRef.Create(tenantId, name, subDomain, nowUtc), ct);
    }

    public async Task<IReadOnlyCollection<Guid>> GetKnownTenantIdsAsync(CancellationToken ct = default) =>
        await dbContext.TenantProfileRefs.IgnoreQueryFilters().Select(r => r.TenantId).ToListAsync(ct);
}
