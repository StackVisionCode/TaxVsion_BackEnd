using BuildingBlocks.Common;
using Microsoft.Extensions.Logging;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Application.Watch;
using Wolverine.Attributes;

namespace TaxVision.Connectors.Application.Sync;

public static class GmailAccountPushReceivedHandler
{
    /// <summary>
    /// Un fallo se registra y no se reintenta acá: el ReconciliationJob recupera lo que un push no trajo.
    /// </summary>
    [NonTransactional]
    public static async Task Handle(
        GmailAccountPushReceived message,
        ITenantEmailAccountRepository accountRepository,
        IProviderWatchSubscriptionRepository subscriptionRepository,
        IProviderSyncCursorRepository cursorRepository,
        IEmailProviderClientFactory emailClientFactory,
        IDistributedLock distributedLock,
        ISyncPendingFlag pendingFlag,
        IOutboxUnit unit,
        ICorrelationContext correlation,
        ILogger logger,
        MessageFetchFailureTracker fetchFailures,
        CancellationToken ct
    )
    {
        using (correlation.Push(message.CorrelationId))
        {
            var result = await ProcessGmailPushNotificationHandler.Handle(
                new ProcessGmailPushNotificationCommand(message.AccountId, message.HistoryId),
                accountRepository,
                subscriptionRepository,
                cursorRepository,
                emailClientFactory,
                distributedLock,
                pendingFlag,
                unit,
                correlation,
                logger,
                fetchFailures,
                ct
            );

            // Una cuenta que ya no existe sigue recibiendo push hasta que vence el watch. No es un fallo.
            if (result.IsFailure && result.Error.Code != "TenantEmailAccount.NotFound")
                logger.LogWarning(
                    "Gmail push processing failed: {Code} {Message}",
                    result.Error.Code,
                    result.Error.Message
                );
        }
    }
}
