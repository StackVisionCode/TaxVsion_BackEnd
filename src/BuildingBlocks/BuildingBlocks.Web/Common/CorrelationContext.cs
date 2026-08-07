using BuildingBlocks.Common;
using Serilog.Context;

namespace BuildingBlocks.Web.Common;

public sealed class CorrelationContext : ICorrelationContext
{
    public string CorrelationId { get; private set; } = string.Empty;

    public void Set(string correlationId) => CorrelationId = correlationId;

    public IDisposable Push(string correlationId)
    {
        Set(correlationId);
        return LogContext.PushProperty("CorrelationId", correlationId);
    }
}
