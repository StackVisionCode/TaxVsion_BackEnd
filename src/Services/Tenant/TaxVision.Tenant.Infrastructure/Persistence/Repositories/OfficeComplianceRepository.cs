using Microsoft.EntityFrameworkCore;
using TaxVision.Tenant.Application.Compliance.Abstractions;
using TaxVision.Tenant.Domain.Compliance;
using TaxVision.Tenant.Domain.Enums;

namespace TaxVision.Tenant.Infrastructure.Persistence.Repositories;

// Mismo criterio que OfficeProfileRepository: tenantId explícito, sin depender del filtro ambiental.
public sealed class OfficeComplianceRepository(TenantDbContext db) : IOfficeComplianceRepository
{
    public Task<OfficeCompliance?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        db.OfficeCompliances.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);

    public async Task AddAsync(OfficeCompliance compliance, CancellationToken ct = default) =>
        await db.OfficeCompliances.AddAsync(compliance, ct);

    public async Task AddRevealRecordAsync(EfinRevealRecord record, CancellationToken ct = default) =>
        await db.EfinRevealRecords.AddAsync(record, ct);

    // Job sin tenant ambiental: IgnoreQueryFilters o el filtro fail-closed devuelve cero filas siempre.
    public async Task<IReadOnlyList<WispReminderCandidate>> ListWispReminderCandidatesAsync(
        DateOnly baseOnOrBefore,
        Guid? afterTenantId,
        int take,
        CancellationToken ct = default
    )
    {
        var query =
            from compliance in db.OfficeCompliances.IgnoreQueryFilters()
            join tenant in db.Tenants on compliance.TenantId equals tenant.Id
            where
                tenant.Status == EnumTenantStatus.TenantStatus.Active
                && (compliance.WispLastReviewedOn ?? compliance.WispAdoptedOn) <= baseOnOrBefore
            select new { compliance.TenantId, tenant.DefaultTimeZoneId };

        if (afterTenantId is { } after)
            query = query.Where(c => c.TenantId.CompareTo(after) > 0);

        var rows = await query.OrderBy(c => c.TenantId).Take(take).AsNoTracking().ToListAsync(ct);
        return rows.Select(r => new WispReminderCandidate(r.TenantId, r.DefaultTimeZoneId)).ToList();
    }
}
