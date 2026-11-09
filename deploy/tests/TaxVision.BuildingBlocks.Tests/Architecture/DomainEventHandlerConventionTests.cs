using BuildingBlocks.Domain;
using TaxVision.BuildingBlocks.Testing.Messaging;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Architecture;

/// <summary>
/// La regla reutilizable de BB-1 (<see cref="DomainEventHandlerConventionRules"/>) contra tipos de muestra: los
/// servicios que adopten BB-1 la heredan con sus ensamblados.
/// </summary>
public sealed class DomainEventHandlerConventionTests
{
    [Fact]
    public void EventWithoutHandler_IsReported()
    {
        var missing = DomainEventHandlerConventionRules.FindEventsWithoutHandler(
            [typeof(HandledSampleEvent), typeof(OrphanSampleEvent), typeof(AbstractSampleEvent)],
            [typeof(HandledSampleEventHandler)]
        );

        Assert.Equal([typeof(OrphanSampleEvent)], missing);
    }

    [Fact]
    public void HandlerThatWolverineDoesNotDiscover_DoesNotCount()
    {
        var missing = DomainEventHandlerConventionRules.FindEventsWithoutHandler(
            [typeof(HandledSampleEvent)],
            [typeof(HandledSampleEventHandlers)]
        );

        Assert.Equal([typeof(HandledSampleEvent)], missing);
    }

    [Fact]
    public void CatchAllDomainEventHandler_DoesNotCount()
    {
        var missing = DomainEventHandlerConventionRules.FindEventsWithoutHandler(
            [typeof(OrphanSampleEvent)],
            [typeof(AnyDomainEventHandler)]
        );

        Assert.Equal([typeof(OrphanSampleEvent)], missing);
    }

    public sealed record HandledSampleEvent(Guid Id) : IDomainEvent;

    public sealed record OrphanSampleEvent(Guid Id) : IDomainEvent;

    public abstract record AbstractSampleEvent : IDomainEvent;

    public sealed record SampleIntegrationEvent(Guid Id);

    public static class HandledSampleEventHandler
    {
        public static SampleIntegrationEvent Handle(HandledSampleEvent domainEvent) => new(domainEvent.Id);
    }

    // Nombre en plural: compila, pero Wolverine no lo descubre.
    public static class HandledSampleEventHandlers
    {
        public static SampleIntegrationEvent Handle(HandledSampleEvent domainEvent) => new(domainEvent.Id);
    }

    public static class AnyDomainEventHandler
    {
        public static void Handle(IDomainEvent domainEvent) { }
    }
}
