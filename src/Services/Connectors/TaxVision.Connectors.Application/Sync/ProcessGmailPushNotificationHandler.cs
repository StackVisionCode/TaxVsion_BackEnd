using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Application.Watch;
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
        var clientResult = emailClientFactory.Resolve(account.ProviderCode);
        if (clientResult.IsFailure)
            return Result.Failure(clientResult.Error);

        // WHY: Gmail Pub/Sub delivers at-least-once — the exact same push notification can arrive
        // twice within milliseconds of each other. Without this lock, two deliveries for the same
        // account could both read the current ProviderSyncCursor, both fetch overlapping history
        // ranges, and both publish raw_message_received for the same underlying message. A
        // redelivery that finds the lock already held skips cleanly (Result.Success — not a
        // failure): the in-flight sync already covers whatever this delivery would have done, and
        // returning a failure here would just make Pub/Sub retry pointlessly. This uses its own
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
            logger.LogInformation(
                "Gmail push redelivery for account {AccountId} arrived while a sync was already in flight — skipping.",
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

        return syncResult.IsFailure ? Result.Failure(syncResult.Error) : Result.Success();
    }
}
