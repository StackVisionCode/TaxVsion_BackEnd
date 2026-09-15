using BuildingBlocks.Common;

namespace TaxVision.Subscription.Tests.TestDoubles;

public sealed class FakeCorrelationContext : ICorrelationContext
{
    public string CorrelationId => "test-correlation-id";

    public void Set(string correlationId) { }

    public IDisposable Push(string correlationId) => new NoopScope();

    private sealed class NoopScope : IDisposable
    {
        public void Dispose() { }
    }
}
