using System.Reflection.Metadata;

namespace BuildingBlocks.Domain;

public abstract class BaseEntity
{


#pragma warning disable IDE0028 // Simplify collection initialization
    private readonly List<IDomainEvent> _domainEvents = new();
#pragma warning restore IDE0028 // Simplify collection initialization

    public Guid Id { get; protected set; } = Guid.NewGuid();
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();



}
