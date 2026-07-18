using Microsoft.EntityFrameworkCore;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Infrastructure.Persistence.Repositories;

public sealed class TenantLogoRefRepository(ScribeDbContext dbContext) : ITenantLogoRefRepository
{
    public Task<TenantLogoRef?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        dbContext.TenantLogoRefs.FirstOrDefaultAsync(r => r.TenantId == tenantId, ct);

    public async Task AddAsync(TenantLogoRef logoRef, CancellationToken ct = default) =>
        await dbContext.TenantLogoRefs.AddAsync(logoRef, ct);
}
