using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Domain.Accounts;
using Wolverine;

namespace TaxVision.Connectors.Application.Sync;

public static class ProcessGmailPushNotificationHandler
{
    /// <summary>
    /// Fase 4 (hardening): TTL del lock de sync por cuenta. Un push puede traer varios mensajes
    /// nuevos, y el orquestador los procesa secuencialmente (un GetMessageAsync por mensaje) — más
    /// generoso que el TTL de 30s de OAuthTokenManager (que solo cubre un refresh puntual) para no
    /// expirar a mitad de un backlog grande y dejar entrar una redelivery a mitad de proceso.
    /// </summary>
    private static readonly TimeSpan WebhookSyncLockTtl = TimeSpan.FromMinutes(2);

    public static async Task<Result> Handle(
        ProcessGmailPushNotificationCommand cmd,
        ITenantEmailAccountRepository accountRepository,
        IProviderWatchSubscriptionRepository subscriptionRepository,
        IProviderSyncCursorRepository cursorRepository,
        IEmailProviderClientFactory emailClientFactory,
        IDistributedLock distributedLock,
        ISyncPendingFlag pendingFlag,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger logger,
        CancellationToken ct
    )
    {
        var accountResult = await accountRepository.GetByEmailAddressAsync(cmd.EmailAddress, ct);
        if (accountResult.IsFailure)
            return Result.Failure(accountResult.Error);

        var account = accountResult.Value;

        // Una cuenta en Error o desconectada no puede sincronizar hasta que el usuario la reconecte.
        // Devolver éxito hace que Pub/Sub deje de reintentar: el 500 generaba un reintento por
        // segundo, y cada uno volvía a intentar el refresh del token.
        if (account.Status is not (TenantEmailAccountStatus.Active or TenantEmailAccountStatus.Connected))
        {
            logger.LogInformation(
                "Gmail push for account {AccountId} ignored — account is {Status}.",
                account.Id,
                account.Status
            );
            return Result.Success();
        }

        var clientResult = emailClientFactory.Resolve(account.ProviderCode);
        if (clientResult.IsFailure)
            return Result.Failure(clientResult.Error);

        // WHY: Gmail Pub/Sub delivers at-least-once — the exact same push notification can arrive
        // twice within milliseconds of each other. Without this lock, two deliveries for the same
        // account could both read the current ProviderSyncCursor, both fetch overlapping history
        // ranges, and both publish raw_message_received for the same underlying message. A
        // delivery that finds the lock already held marks the account as pending and returns
        // Result.Success: the lock owner runs one more pass, and a failure would only make Pub/Sub
        // retry pointlessly. This uses its own
        // "webhook-sync" lock namespace, deliberately separate from OAuthTokenManager's
        // "oauth-refresh" one — the two never need to serialize against each other directly. The
        // provider clients invoked below call IOAuthTokenManager internally for a valid access
        // token, so an oauth-refresh acquisition always nests INSIDE this webhook-sync lock, in
        // that fixed order, from a single call stack — never the other way around — so there is no
        // lock-order-inversion risk from keeping them separate, and sharing one key would only add
        // unrelated contention between unrelated operations.
        var lockKey = $"connectors:webhook-sync:{account.Id:N}";
        await using var lockHandle = await distributedLock.AcquireAsync(lockKey, WebhookSyncLockTtl, ct);
        if (!lockHandle.IsAcquired)
        {
            // Puede ser un mensaje nuevo que el sync en vuelo ya no ve: el dueño del lock repite.
            await pendingFlag.MarkAsync(account.Id, ct);
            logger.LogInformation(
                "Gmail push for account {AccountId} arrived while a sync was already in flight — marked as pending.",
                account.Id
            );
            return Result.Success();
        }

        var (cursor, _) = await ProviderSyncCursorSeeder.GetOrSeedAsync(
            account,
            cmd.HistoryId,
            subscriptionRepository,
            cursorRepository,
            DateTime.UtcNow,
            ct
        );

        var syncResult = await PendingAwareSync.RunAsync(
            account.Id,
            lockHandle,
            WebhookSyncLockTtl,
            pendingFlag,
            () =>
                RawMessageSyncOrchestrator.SyncAndPublishAsync(
                    account,
                    clientResult.Value,
                    cursor,
                    unitOfWork,
                    bus,
                    correlation,
                    logger,
                    ct
                ),
            ct
        );

        if (syncResult.IsFailure && syncResult.Error.Code == RawMessageSyncOrchestrator.ReconnectRequiredCode)
        {
            logger.LogWarning(
                "Gmail push for account {AccountId} acknowledged without sync — the account needs to be reconnected.",
                account.Id
            );
            return Result.Success();
        }

        return syncResult.IsFailure ? Result.Failure(syncResult.Error) : Result.Success();
    }
}
