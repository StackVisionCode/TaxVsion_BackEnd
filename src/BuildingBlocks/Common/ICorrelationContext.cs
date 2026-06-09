namespace BuildingBlocks;

public interface ICorrelationContext
{

    string CorrelationId { get; }
    void Set(string correlationId);

}

public sealed class CorrelationContext : ICorrelationContext
{
    public string CorrelationId { get; private set; } = string.Empty;
    public void Set(string correlationId) => CorrelationId = correlationId;


}
