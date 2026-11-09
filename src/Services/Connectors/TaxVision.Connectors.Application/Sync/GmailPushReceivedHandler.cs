using TaxVision.Connectors.Application.Accounts;
using Wolverine;

namespace TaxVision.Connectors.Application.Sync;

public static class GmailPushReceivedHandler
{
    /// <summary>
    /// El push solo trae la dirección, y el mismo buzón puede estar conectado en varias oficinas: se encola uno por
    /// cuenta. Sin ninguna sincronizable no sale nada, y el webhook ya respondió éxito (corta los reintentos de Pub/Sub).
    /// </summary>
    public static async Task<OutgoingMessages> Handle(
        GmailPushReceived message,
        ITenantEmailAccountRepository accountRepository,
        CancellationToken ct
    )
    {
        var accountIds = await accountRepository.ListSyncableGmailByEmailAddressAsync(message.EmailAddress, ct);

        var outgoing = new OutgoingMessages();
        outgoing.AddRange(
            accountIds.Select(accountId => new GmailAccountPushReceived(
                accountId,
                message.HistoryId,
                message.CorrelationId
            ))
        );
        return outgoing;
    }
}
