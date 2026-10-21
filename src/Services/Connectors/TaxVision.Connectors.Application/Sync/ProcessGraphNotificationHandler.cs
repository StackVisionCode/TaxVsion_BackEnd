using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Watch;
using Wolverine;

namespace TaxVision.Connectors.Application.Sync;

public static class ProcessGraphNotificationHandler
{
    /// <summary>Fase 4 (hardening): mismo TTL y mismo razonamiento que ProcessGmailPushNotificationHandler.</summary>
    private static readonly TimeSpan WebhookSyncLockTtl = TimeSpan.FromMinutes(2);

    public static async Task<Result> Handle(
        ProcessGraphNotificationCommand cmd,
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
        var subscriptionResult = await subscriptionRepository.GetBySubscriptionRefAsync(cmd.SubscriptionId, ct);
        if (subscriptionResult.IsFailure)
            return Result.Failure(subscriptionResult.Error);

        // A diferencia de Gmail, Graph identifica la notificación por subscription y no por buzón.
        // Una subscription que ya dimos por perdida (Failed: el refresh o la renovación fallaron) sigue
        // notificando hasta que vence en Microsoft (máx. ~70 h): se descarta sin cargar la cuenta ni
        // pedir token. Al reconectar se crea una subscription nueva con otro id.
        var subscription = subscriptionResult.Value;
        if (subscription.Status != ProviderWatchStatus.Active)
        {
            logger.LogInformation(
                "Graph notification for subscription {SubscriptionId} ignored — subscription is {Status}.",
                subscription.Id,
                subscription.Status
            );
            return Result.Success();
        }

        var accountResult = await accountRepository.GetByIdAsync(subscription.AccountId, ct);
        if (accountResult.IsFailure)
            return Result.Failure(accountResult.Error);

        var account = accountResult.Value;

        // Mismo criterio que Gmail: sin reconectar no hay token con qué sincronizar. Responder rápido
        // también importa acá: Graph espera la respuesta en ~3 s y degrada los endpoints lentos.
        if (account.Status is not (TenantEmailAccountStatus.Active or TenantEmailAccountStatus.Connected))
        {
            logger.LogInformation(
                "Graph notification for account {AccountId} ignored — account is {Status}.",
                account.Id,
                account.Status
            );
            return Result.Success();
        }
        var clientResult = emailClientFactory.Resolve(account.ProviderCode);
        if (clientResult.IsFailure)
            return Result.Failure(clientResult.Error);

        // WHY: same at-least-once race as ProcessGmailPushNotificationHandler — Graph subscriptions
        // can and do redeliver the same notification. Same lock namespace convention
        // ("connectors:webhook-sync:{accountId}") so a Gmail and a Graph account never share a key
        // by construction (each TenantEmailAccount has exactly one ProviderCode), and same
        // skip-cleanly-on-contention behavior: a redelivery that loses the race returns
        // Result.Success, it does not retry or fail loudly.
        var lockKey = $"connectors:webhook-sync:{account.Id:N}";
        await using var lockHandle = await distributedLock.AcquireAsync(lockKey, WebhookSyncLockTtl, ct);
        if (!lockHandle.IsAcquired)
        {
            logger.LogInformation(
                "Graph notification redelivery for account {AccountId} arrived while a sync was already in flight — skipping.",
                account.Id
            );
            return Result.Success();
        }

        // Graph maneja cursor null sin problema (arranca desde la URL base del delta query) — a
        // diferencia de Gmail, no hace falta sembrarlo (ver ProviderSyncCursorSeeder).
        var (cursor, _) = await ProviderSyncCursorSeeder.GetOrSeedAsync(
            account,
            fallbackCursorValue: null,
            subscriptionRepository,
            cursorRepository,
            DateTime.UtcNow,
            ct
        );

        var syncResult = await RawMessageSyncOrchestrator.SyncAndPublishAsync(
            account,
            clientResult.Value,
            cursor,
            unitOfWork,
            bus,
            correlation,
            logger,
            ct
        );

        // El controller de Graph responde 200 siempre (Graph no reintenta como Pub/Sub); esto solo
        // evita registrar como fallo cada notificación de una cuenta que necesita reconectarse.
        if (syncResult.IsFailure && syncResult.Error.Code == RawMessageSyncOrchestrator.ReconnectRequiredCode)
        {
            logger.LogWarning(
                "Graph notification for account {AccountId} acknowledged without sync — the account needs to be reconnected.",
                account.Id
            );
            return Result.Success();
        }

        return syncResult.IsFailure ? Result.Failure(syncResult.Error) : Result.Success();
    }
}
