using Microsoft.EntityFrameworkCore;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Infrastructure.Persistence.Repositories;

public sealed class SystemAssetRefRepository(ScribeDbContext dbContext) : ISystemAssetRefRepository
{
    public Task<SystemAssetRef?> GetByKeyAsync(string key, CancellationToken ct = default) =>
        dbContext.SystemAssetRefs.FirstOrDefaultAsync(r => r.Key == key, ct);

    public async Task AddAsync(SystemAssetRef assetRef, CancellationToken ct = default) =>
        await dbContext.SystemAssetRefs.AddAsync(assetRef, ct);
}
