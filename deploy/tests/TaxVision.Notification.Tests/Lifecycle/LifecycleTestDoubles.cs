using BuildingBlocks.Common;
using BuildingBlocks.Persistence;

namespace TaxVision.Notification.Tests.Lifecycle;

internal sealed class CountingUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCount++;
        return Task.FromResult(1);
    }
}

internal sealed class NoOpCorrelation : ICorrelationContext
{
    public string CorrelationId { get; private set; } = string.Empty;

    public void Set(string correlationId) => CorrelationId = correlationId;

    public IDisposable Push(string correlationId)
    {
        CorrelationId = correlationId;
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose() { }
    }
}
