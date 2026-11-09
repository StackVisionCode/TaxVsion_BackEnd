using BuildingBlocks.Persistence;

namespace TaxVision.Wallet.Tests.TestDoubles;

/// <summary>Cuenta las llamadas a SaveChangesAsync.</summary>
public sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCount++;
        return Task.FromResult(1);
    }
}
