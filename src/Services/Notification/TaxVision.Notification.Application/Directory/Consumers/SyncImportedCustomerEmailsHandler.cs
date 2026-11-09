using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using TaxVision.Notification.Application.Directory.Abstractions;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Notification.Application.Directory.Consumers;

/// <summary>Clientes de una importación que faltan en el directorio. <see cref="Page"/> es la página de Customer a leer.</summary>
public sealed record SyncImportedCustomerEmailsCommand(Guid TenantId, IReadOnlyList<Guid> CustomerIds, int Page = 1);

/// <summary>
/// NOT-21: <c>CustomersBulkImported</c> no trae correos, así que se piden a Customer, una página por transacción y sin
/// transacción abierta durante la llamada. Sigue hasta encontrarlos a todos o agotar el tenant; lo que no aparezca
/// lo completa la reconciliación.
/// </summary>
public static class SyncImportedCustomerEmailsHandler
{
    [NonTransactional]
    public static async Task Handle(
        SyncImportedCustomerEmailsCommand command,
        INotificationCustomerClient customerClient,
        ICustomerEmailDirectoryRepository directory,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var page =
            await customerClient.ListTenantCustomersAsync(command.TenantId, command.Page, ct)
            ?? throw new DependencyNotReadyException("Customer.Api", $"Customer page {command.Page} is not available.");

        var pending = command.CustomerIds.ToHashSet();
        foreach (var contact in page.Items.Where(x => pending.Contains(x.CustomerId)))
        {
            await CustomerEmailDirectoryConsumer.ApplyAsync(
                directory,
                command.TenantId,
                contact.CustomerId,
                contact.PrimaryEmail,
                contact.DisplayName,
                contact.IsActive,
                ct
            );
            pending.Remove(contact.CustomerId);
        }
        await unitOfWork.SaveChangesAsync(ct);

        if (pending.Count > 0 && page.HasMore)
            await bus.PublishAsync(command with { CustomerIds = pending.ToList(), Page = command.Page + 1 });
    }
}
