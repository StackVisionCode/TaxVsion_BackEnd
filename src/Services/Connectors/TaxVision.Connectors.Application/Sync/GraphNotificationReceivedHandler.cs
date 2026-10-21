using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Application.Watch;
using Wolverine;

namespace TaxVision.Connectors.Application.Sync;

public static class GraphNotificationReceivedHandler
{
    /// <summary>
    /// Mismo resultado que cuando el webhook procesaba en línea: un fallo se registra y no se
    /// reintenta. El ReconciliationJob (cada 15 min) recupera lo que una notificación no trajo.
    /// </summary>
    public static async Task Handle(
        GraphNotificationReceived message,
        IProviderWatchSubscriptionRepository subscriptionRepository,
        ITenantEmailAccountRepository accountRepository,
        IProviderSyncCursorRepository cursorRepository,
        IEmailProviderClientFactory emailClientFactory,
        IDistributedLock distributedLock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(message.CorrelationId))
        {
            var result = await ProcessGraphNotificationHandler.Handle(
                new ProcessGraphNotificationCommand(message.SubscriptionId),
                subscriptionRepository,
                accountRepository,
                cursorRepository,
                emailClientFactory,
                distributedLock,
                unitOfWork,
                bus,
                correlation,
                logger,
                ct
            );

            // NotFound: una subscription vieja que ya reemplazamos (reconexión) sigue notificando
            // hasta vencer en Microsoft. No es un fallo.
            if (result.IsFailure && result.Error.Code != "ProviderWatchSubscription.NotFound")
                logger.LogWarning(
                    "Graph notification processing failed: {Code} {Message}",
                    result.Error.Code,
                    result.Error.Message
                );
        }
    }
}
