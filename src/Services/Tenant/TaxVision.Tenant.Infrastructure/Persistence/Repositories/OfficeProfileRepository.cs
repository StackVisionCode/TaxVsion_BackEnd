using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tenant.Application.Profiles.Abstractions;
using TaxVision.Tenant.Domain.Profiles;

namespace TaxVision.Tenant.Infrastructure.Persistence.Repositories;

// Los handlers corren en scopes de Wolverine sin tenant ambiental: el filtro global dejaría cero filas,
// así que cada consulta ignora el filtro y filtra por el tenantId explícito (JWT o evento).
public sealed class OfficeProfileRepository(TenantDbContext db) : IOfficeProfileRepository
{
    public Task<OfficeProfile?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        db.OfficeProfiles.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

    public async Task AddAsync(OfficeProfile profile, CancellationToken ct = default) =>
        await db.OfficeProfiles.AddAsync(profile, ct);

    public async Task<IReadOnlyList<OfficeProfile>> ListSavedAsync(
        int skip,
        int take,
        CancellationToken ct = default
    ) =>
        await db
            .OfficeProfiles.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.UpdatedAtUtc != null)
            .OrderBy(p => p.TenantId)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    public async Task<ImportCandidates> ClassifyImportCandidatesAsync(
        IReadOnlyCollection<Guid> tenantIds,
        CancellationToken ct = default
    )
    {
        var ids = tenantIds.ToList();
        var offices = await db
            .Tenants.Where(t => ids.Contains(t.Id) && t.Kind == TenantKind.Customer)
            .Select(t => new { t.Id, HasProfile = db.OfficeProfiles.IgnoreQueryFilters().Any(p => p.TenantId == t.Id) })
            .ToListAsync(ct);
        return new ImportCandidates(
            offices.Select(o => o.Id).ToHashSet(),
            offices.Where(o => o.HasProfile).Select(o => o.Id).ToHashSet()
        );
    }
}
