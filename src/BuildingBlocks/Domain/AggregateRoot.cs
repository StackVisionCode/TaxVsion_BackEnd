namespace BuildingBlocks.Domain;

/// <summary>
/// Entidad tenant-owned que además acumula domain events entre su mutación y el
/// momento en que se persiste. El agregado no conoce Wolverine ni el bus — solo
/// junta hechos; drenarlos y despacharlos es responsabilidad exclusiva del DbContext
/// (ver AuthDbContext.SaveChangesAsync), siempre antes de confirmar la transacción.
/// </summary>
public abstract class AggregateRoot : TenantEntity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
