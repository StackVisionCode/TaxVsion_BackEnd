namespace BuildingBlocks.Domain;

/// <summary>
/// Raíz de agregado sin <c>TenantId</c>, para el agregado que es el tenant mismo (Tenant). Decisión D-TEN-1:
/// variante aditiva, <see cref="AggregateRoot"/> no cambia. Junta eventos igual que ella y
/// <c>UseTaxVisionDomainEvents()</c> despacha las dos.
/// </summary>
public abstract class TenantlessAggregateRoot : BaseEntity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
