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

        var accountResult = await accountRepository.GetByIdAsync(subscriptionResult.Value.AccountId, ct);
        if (accountResult.IsFailure)
            return Result.Failure(accountResult.Error);

        var account = accountResult.Value;
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

        return syncResult.IsFailure ? Result.Failure(syncResult.Error) : Result.Success();
    }
}
