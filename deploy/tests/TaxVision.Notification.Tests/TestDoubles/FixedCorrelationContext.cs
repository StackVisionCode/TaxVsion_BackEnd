using BuildingBlocks.Common;

namespace TaxVision.Notification.Tests.TestDoubles;

/// <summary>Correlación fija para consumers que solo la empujan.</summary>
internal sealed class FixedCorrelationContext : ICorrelationContext
{
    public string CorrelationId => "test";

    public IDisposable Push(string correlationId) => new Scope();

    public void Set(string correlationId) { }

    private sealed class Scope : IDisposable
    {
        public void Dispose() { }
    }
}
