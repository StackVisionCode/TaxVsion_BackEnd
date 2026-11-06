using BuildingBlocks.Domain;
using TaxVision.Correspondence.Application.Inbox.DomainEvents;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Tests.Ingest;

namespace TaxVision.Correspondence.Tests;

/// <summary>
/// Lo que Wolverine hace con UseTaxVisionDomainEvents(), en memoria: corre los handlers de dominio sobre los eventos
/// pendientes de hilos y correos, y deja lo que devuelven en el bus de la prueba. Así la prueba ve lo emitido por el
/// comando: lo publicado a mano más lo devuelto por los handlers.
/// </summary>
internal static class DomainEventReplay
{
    public static async Task DispatchIntoAsync(
        FakeMessageBus bus,
        FakeEmailThreadRepository threads,
        FakeIncomingEmailRepository incomingEmails
    )
    {
        // El comando no publica eventos de dominio: se comprueba antes de sumar lo devuelto.
        Assert.DoesNotContain(bus.Published, message => message is IDomainEvent);

        foreach (var root in threads.All.Cast<AggregateRoot>().Concat(incomingEmails.All))
        {
            foreach (var domainEvent in root.DomainEvents)
            {
                var notice = await RunHandlerAsync(domainEvent, threads, incomingEmails);
                if (notice is not null)
                    await bus.PublishAsync(notice);
            }
            root.ClearDomainEvents();
        }
    }

    private static async Task<object?> RunHandlerAsync(
        IDomainEvent domainEvent,
        FakeEmailThreadRepository threads,
        FakeIncomingEmailRepository incomingEmails
    ) =>
        domainEvent switch
        {
            IncomingEmailReceivedDomainEvent received => IncomingEmailReceivedDomainEventHandler.Handle(received),
            EmailThreadStateChangedDomainEvent changed => await EmailThreadStateChangedDomainEventHandler.Handle(
                changed,
                threads,
                incomingEmails,
                CancellationToken.None
            ),
            _ => throw new InvalidOperationException($"Sin handler de dominio: {domainEvent.GetType().Name}"),
        };
}
