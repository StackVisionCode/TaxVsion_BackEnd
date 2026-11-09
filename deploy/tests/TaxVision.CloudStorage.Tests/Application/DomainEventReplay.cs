using BuildingBlocks.Domain;
using TaxVision.CloudStorage.Application.Files.DomainEvents;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Files.Events;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>
/// Lo que Wolverine hace con UseTaxVisionDomainEvents(), en memoria: saca los eventos pendientes del archivo, corre sus
/// handlers y deja lo que devuelven en el bus. La prueba ve lo publicado a mano más lo que sale del dominio.
/// </summary>
internal static class DomainEventReplay
{
    public static async Task DispatchIntoAsync(FakeMessageBus bus, FileObject file)
    {
        var events = file.DomainEvents.ToList();
        file.ClearDomainEvents();
        foreach (var domainEvent in events)
        foreach (var message in Messages(domainEvent))
            await bus.PublishAsync(message);
    }

    private static IEnumerable<object> Messages(IDomainEvent domainEvent) =>
        domainEvent switch
        {
            FileBecameAvailableDomainEvent available => FileBecameAvailableDomainEventHandler.Handle(available),
            FileQuarantinedDomainEvent quarantined => FileQuarantinedDomainEventHandler.Handle(quarantined),
            FileScanFailedDomainEvent failed => [FileScanFailedDomainEventHandler.Handle(failed)],
            _ => throw new NotSupportedException(domainEvent.GetType().Name),
        };
}
