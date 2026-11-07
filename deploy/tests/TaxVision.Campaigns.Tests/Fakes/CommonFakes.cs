using BuildingBlocks.Common;
using BuildingBlocks.Persistence;

namespace TaxVision.Campaigns.Tests.Fakes;

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveChangesCallCount { get; private set; }

    /// <summary>La próxima confirmación falla, como un choque de la base.</summary>
    public bool FailNextSave { get; set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        if (FailNextSave)
        {
            FailNextSave = false;
            throw new InvalidOperationException("Simulated save failure.");
        }

        SaveChangesCallCount++;
        return Task.FromResult(0);
    }
}

internal sealed class FakeCorrelationContext : ICorrelationContext
{
    public string CorrelationId { get; private set; } = "test-correlation";

    public void Set(string correlationId) => CorrelationId = correlationId;

    public IDisposable Push(string correlationId)
    {
        var previous = CorrelationId;
        CorrelationId = correlationId;
        return new Popper(this, previous);
    }

    private sealed class Popper(FakeCorrelationContext owner, string previous) : IDisposable
    {
        public void Dispose() => owner.CorrelationId = previous;
    }
}
