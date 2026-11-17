using Microsoft.EntityFrameworkCore;

namespace TaxVision.Growth.Infrastructure.Persistence.Referrals;

/// <summary>Lectura y alta de la proyección de nombres de oficina. Es de sistema: no se acota al tenant activo.</summary>
public sealed class ReferredOfficeProjectionStore(GrowthDbContext db)
{
    public async Task<ReferredOfficeProjection> GetOrCreateAsync(Guid officeTenantId, CancellationToken ct)
    {
        var existing = await db
            .ReferredOffices.IgnoreQueryFilters()
            .SingleOrDefaultAsync(office => office.TenantId == officeTenantId, ct);
        if (existing is not null)
            return existing;

        var created = ReferredOfficeProjection.Create(officeTenantId);
        await db.ReferredOffices.AddAsync(created, ct);
        return created;
    }
}
