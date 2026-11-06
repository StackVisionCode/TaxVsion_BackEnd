using BuildingBlocks.Domain;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Application.Sending.DomainEvents;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Tests.Consumers;

/// <summary>
/// Lo que Wolverine hace con UseTaxVisionDomainEvents(), en memoria: corre los handlers de dominio sobre los eventos
/// pendientes de los mensajes y deja lo que devuelven en el bus de la prueba. Así la prueba ve lo emitido por el
/// comando: lo publicado a mano más lo devuelto por los handlers.
/// </summary>
internal static class DomainEventReplay
{
    public static async Task DispatchIntoAsync(FakeMessageBus bus, ISentMessageRepository sentMessages)
    {
        // El comando no publica eventos de dominio: se comprueba antes de sumar lo devuelto.
        Assert.DoesNotContain(bus.Published, message => message is IDomainEvent);

        if (sentMessages is not FakeSentMessageRepository fake)
            return;

        foreach (var message in fake.Added)
        {
            foreach (var domainEvent in message.DomainEvents)
            {
                var notice = Run(domainEvent);
                if (notice is not null)
                    await bus.PublishAsync(notice);
            }
            message.ClearDomainEvents();
        }
    }

    private static object? Run(IDomainEvent domainEvent) =>
        domainEvent switch
        {
            SentMessageSentDomainEvent sent => SentMessageSentDomainEventHandler.Handle(sent),
            SentMessageFailedDomainEvent failed => SentMessageFailedDomainEventHandler.Handle(failed),
            SentMessageSuppressedDomainEvent suppressed => SentMessageSuppressedDomainEventHandler.Handle(suppressed),
            _ => throw new InvalidOperationException($"Sin handler de dominio: {domainEvent.GetType().Name}"),
        };
}
