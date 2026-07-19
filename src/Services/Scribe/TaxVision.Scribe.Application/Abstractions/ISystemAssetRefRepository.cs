using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Application.Abstractions;

public interface ISystemAssetRefRepository
{
    Task<SystemAssetRef?> GetByKeyAsync(string key, CancellationToken ct = default);
    Task AddAsync(SystemAssetRef assetRef, CancellationToken ct = default);
}
