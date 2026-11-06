using BuildingBlocks.Domain;
using TaxVision.Connectors.Application.Accounts.DomainEvents;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Tests.OAuth;

namespace TaxVision.Connectors.Tests.Accounts;

/// <summary>
/// Lo que Wolverine hace con UseTaxVisionDomainEvents(), en memoria: corre el handler de dominio sobre los eventos
/// pendientes. La prueba ve lo emitido por el comando: lo publicado a mano más lo devuelto por el handler.
/// </summary>
internal static class DomainEventReplay
{
    public static IReadOnlyList<object> Emitted(FakeMessageBus bus, params AggregateRoot[] roots) =>
        [
            .. bus.Published,
            .. roots
                .SelectMany(root => root.DomainEvents)
                .OfType<TenantEmailAccountErroredDomainEvent>()
                .Select(TenantEmailAccountErroredDomainEventHandler.Handle),
        ];

    /// <summary>Ningún evento de dominio sale por el bus: va a la cola local con el commit.</summary>
    public static void AssertNoDomainEventOnBus(FakeMessageBus bus) =>
        Assert.DoesNotContain(bus.Published, message => message is IDomainEvent);
}
