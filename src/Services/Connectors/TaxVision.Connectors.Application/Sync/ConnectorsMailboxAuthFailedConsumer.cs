using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Domain.Shared;
using Wolverine;

namespace TaxVision.Connectors.Application.Sync;

/// <summary>
/// El listener vio que el servidor IMAP rechaza la contraseña. Se marca la cuenta y se avisa una sola
/// vez, igual que cuando lo detecta la reconciliación (el listener no escribe en la base).
/// </summary>
public static class ConnectorsMailboxAuthFailedConsumer
{
    public static async Task Handle(
        ConnectorsMailboxAuthFailedIntegrationEvent evt,
        ITenantEmailAccountRepository accountRepository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            var account = await accountRepository.GetByIdAsync(evt.AccountId, ct);
            if (account.IsFailure || account.Value.ProviderCode != ProviderCode.Imap)
                return;

            await MailboxCredentialsRejection.HandleAsync(
                account.Value,
                "The IMAP server rejected the username or password.",
                unitOfWork,
                bus,
                correlation,
                logger,
                ct
            );
        }
    }
}
