using BuildingBlocks.Common;

namespace TaxVision.Wallet.Tests.TestDoubles;

public sealed class FakeCorrelationContext : ICorrelationContext
{
    public string CorrelationId { get; private set; } = "test-correlation";

    public void Set(string correlationId) => CorrelationId = correlationId;

    public IDisposable Push(string correlationId)
    {
        var previous = CorrelationId;
        CorrelationId = correlationId;
        return new Restore(() => CorrelationId = previous);
    }

    private sealed class Restore(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
