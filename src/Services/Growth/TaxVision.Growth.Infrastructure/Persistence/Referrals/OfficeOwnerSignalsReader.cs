using Microsoft.EntityFrameworkCore;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Domain.Fraud;

namespace TaxVision.Growth.Infrastructure.Persistence.Referrals;

/// <summary>Lee la identidad del dueño guardada en la proyección de oficinas (solo HMAC y marca de desechable).</summary>
public sealed class OfficeOwnerSignalsReader(GrowthDbContext db) : IOfficeOwnerSignalsReader
{
    public async Task<OfficeOwnerSignals?> ForOfficeAsync(Guid officeTenantId, CancellationToken ct = default)
    {
        var office = await db
            .ReferredOffices.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == officeTenantId && item.OwnerSubjectId != null)
            .Select(item => new { item.OwnerSubjectId, item.OwnerEmailDisposable })
            .FirstOrDefaultAsync(ct);
        return office is null ? null : new OfficeOwnerSignals(office.OwnerSubjectId, office.OwnerEmailDisposable);
    }
}
