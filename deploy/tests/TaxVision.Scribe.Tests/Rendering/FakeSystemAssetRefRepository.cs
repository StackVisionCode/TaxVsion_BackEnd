using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Tests.Rendering;

internal sealed class FakeSystemAssetRefRepository : ISystemAssetRefRepository
{
    private readonly Dictionary<string, SystemAssetRef> _store = new();

    public static FakeSystemAssetRefRepository WithHeaderLogo(SystemAssetRef assetRef)
    {
        var repository = new FakeSystemAssetRefRepository();
        repository._store[assetRef.Key] = assetRef;
        return repository;
    }

    public Task<SystemAssetRef?> GetByKeyAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(_store.TryGetValue(key, out var value) ? value : null);

    public Task AddAsync(SystemAssetRef assetRef, CancellationToken ct = default)
    {
        _store[assetRef.Key] = assetRef;
        return Task.CompletedTask;
    }
}
