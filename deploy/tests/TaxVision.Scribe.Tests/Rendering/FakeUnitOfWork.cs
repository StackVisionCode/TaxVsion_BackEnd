using BuildingBlocks.Persistence;

namespace TaxVision.Scribe.Tests.Rendering;

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveChangesCallCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        SaveChangesCallCount++;
        return Task.FromResult(0);
    }
}
