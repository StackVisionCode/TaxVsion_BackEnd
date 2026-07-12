namespace BuildingBlocks.Common;

public interface ICorrelationContext
{
    string CorrelationId { get; }
    void Set(string correlationId);
    IDisposable Push(string correlationId);
}
