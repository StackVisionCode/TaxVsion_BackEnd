using BuildingBlocks.Common;
using Microsoft.Extensions.Logging;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Sync;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Watch;
using Wolverine;

namespace TaxVision.Connectors.Application.Watch;

/// <summary>
/// Sin estos avisos, una subscription que Graph borraba o dejaba de autorizar se descubría recién en
/// la siguiente renovación (hasta ~70 h después), y mientras tanto no llegaba correo nuevo.
/// </summary>
public static class GraphLifecycleNotificationHandler
{
    public static async Task Handle(
        GraphLifecycleNotificationReceived message,
        IProviderWatchSubscriptionRepository subscriptionRepository,
        ITenantEmailAccountRepository accountRepository,
        IWatchRenewalService renewalService,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(message.CorrelationId))
        {
            var subscriptionResult = await subscriptionRepository.GetBySubscriptionRefAsync(message.SubscriptionId, ct);
            // Una subscription vieja (ya reemplazada al reconectar) o una que ya dimos por perdida no
            // se toca: reconectar la cuenta es lo único que la recupera.
            if (subscriptionResult.IsFailure || subscriptionResult.Value.Status != ProviderWatchStatus.Active)
            {
                logger.LogInformation(
                    "Graph lifecycle event {LifecycleEvent} for subscription {SubscriptionRef} ignored — subscription unknown or not active.",
                    message.LifecycleEvent,
                    message.SubscriptionId
                );
                return;
            }

            var subscription = subscriptionResult.Value;
            var accountResult = await accountRepository.GetByIdAsync(subscription.AccountId, ct);
            if (
                accountResult.IsFailure
                || accountResult.Value.Status
                    is not (TenantEmailAccountStatus.Active or TenantEmailAccountStatus.Connected)
            )
            {
                logger.LogInformation(
                    "Graph lifecycle event {LifecycleEvent} for account {AccountId} ignored — account cannot sync.",
                    message.LifecycleEvent,
                    subscription.AccountId
                );
                return;
            }

            switch (message.LifecycleEvent)
            {
                case GraphLifecycleEvents.ReauthorizationRequired:
                    // Renovar (PATCH expirationDateTime) también reautoriza la subscription en Graph.
                    LogIfFailed(await renewalService.RenewAsync(subscription.Id, ct), message, logger);
                    break;

                case GraphLifecycleEvents.SubscriptionRemoved:
                    // Ya no existe en Graph: renovarla daría 404, hay que crear otra.
                    LogIfFailed(await renewalService.RecreateAsync(subscription.Id, ct), message, logger);
                    break;

                case GraphLifecycleEvents.Missed:
                    // El delta query desde el cursor guardado trae lo que no se notificó.
                    await bus.PublishAsync(
                        new GraphNotificationReceived(message.SubscriptionId, message.CorrelationId)
                    );
                    break;

                default:
                    logger.LogWarning(
                        "Graph lifecycle event {LifecycleEvent} for subscription {SubscriptionRef} is not handled.",
                        message.LifecycleEvent,
                        message.SubscriptionId
                    );
                    break;
            }
        }
    }

    private static void LogIfFailed(
        BuildingBlocks.Results.Result result,
        GraphLifecycleNotificationReceived message,
        ILogger logger
    )
    {
        if (result.IsFailure)
            logger.LogWarning(
                "Graph lifecycle event {LifecycleEvent} for subscription {SubscriptionRef} could not be handled: {Code} {Message}",
                message.LifecycleEvent,
                message.SubscriptionId,
                result.Error.Code,
                result.Error.Message
            );
    }
}
