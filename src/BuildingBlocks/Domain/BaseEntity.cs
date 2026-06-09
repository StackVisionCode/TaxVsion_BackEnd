using System.Reflection.Metadata;

namespace BuildingBlocks;

public abstract class BaseEntity
{

    // ReSharper disable once CollectionNeverUpdated.Global

    private readonly List<IDomainEvent> _domainEvents = new();

    public Guid Id { get; protected set; }
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();



}
