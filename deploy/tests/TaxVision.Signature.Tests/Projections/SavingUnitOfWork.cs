using BuildingBlocks.Persistence;

namespace TaxVision.Signature.Tests.Projections;

internal sealed class SavingUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCount++;
        return Task.FromResult(1);
    }
}
