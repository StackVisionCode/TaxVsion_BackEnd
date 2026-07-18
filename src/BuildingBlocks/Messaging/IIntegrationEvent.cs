namespace BuildingBlocks.Messaging;

public interface IIntegrationEvent
{
    Guid EventId { get; }
    Guid TenantId { get; }
    DateTime OccurredOn { get; }
    string CorrelationId { get; }
}

public abstract record IntegrationEvent : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public Guid TenantId { get; init; }

    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;

    public string CorrelationId { get; init; } = string.Empty;
}
