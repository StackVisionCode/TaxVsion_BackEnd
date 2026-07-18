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

/// <summary>
/// Re-invoca RawMessageSyncOrchestrator para UNA cuenta a partir de su ProviderSyncCursor
/// persistido — exactamente el mismo camino que ProcessGmailPushNotificationHandler/
/// ProcessGraphNotificationHandler, solo que disparado por ReconciliationJob (Infra) en vez de por
/// un push/notification entrante. No inventa un mecanismo de sync paralelo, reusa TODO lo que ya
/// protege esos dos handlers:
///   - Mismo lock "connectors:webhook-sync:{accountId}" (Fase 4 hardening) — un pase de
///     reconciliación y un sync disparado por webhook para la MISMA cuenta nunca corren en
///     paralelo, se serializan. Si el lock está tomado, este pase se salta limpio
///     (Result.Success con Skipped=true): el sync en vuelo ya cubre lo que este pase hubiera hecho.
///   - Mismo IEmailProviderClientFactory → mismos GmailApiClient/GraphApiClient/ImapClient → mismo
///     IProviderRateLimiter + ProviderCircuitBreaker que ya los protegen (Fase 10) — la
///     reconciliación nunca le pega a un provider por fuera de esas defensas.
///   - GetHistoryAsync (Gmail history.list / Graph delta / IMAP UID search) ya es incremental por
///     diseño — reconciliar es simplemente "correr el mismo fetch de siempre otra vez" contra el
///     cursor persistido, sin importar qué lo disparó.
/// </summary>
public static class ReconcileAccountHandler
{
    /// <summary>
    /// Mayor que el WebhookSyncLockTtl (2 min) de los webhook handlers — un pase de reconciliación
    /// puede ser el PRIMER sync de una cuenta IMAP recién activada (sin watch, sin push nunca) y
    /// traer de arranque el historial completo del inbox, no solo un puñado de mensajes nuevos como
    /// trae normalmente un push. Mismo namespace de lock ("connectors:webhook-sync:{accountId}")
    /// que esos handlers a propósito — el TTL es per-adquisición, así que cualquiera de los dos
    /// lados serializa correctamente contra el otro sin importar qué TTL usó para tomarlo.
    /// </summary>
    private static readonly TimeSpan ReconciliationSyncLockTtl = TimeSpan.FromMinutes(10);

    public static async Task<Result<ReconciliationOutcome>> Handle(
        ReconcileAccountCommand cmd,
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
        var accountResult = await accountRepository.GetByIdAsync(cmd.AccountId, ct);
        if (accountResult.IsFailure)
            return Result.Failure<ReconciliationOutcome>(accountResult.Error);

        var account = accountResult.Value;
        var clientResult = emailClientFactory.Resolve(account.ProviderCode);
        if (clientResult.IsFailure)
            return Result.Failure<ReconciliationOutcome>(clientResult.Error);

        var lockKey = $"connectors:webhook-sync:{account.Id:N}";
        await using var lockHandle = await distributedLock.AcquireAsync(lockKey, ReconciliationSyncLockTtl, ct);
        if (!lockHandle.IsAcquired)
        {
            logger.LogInformation(
                "Reconciliation for account {AccountId} skipped — a webhook-triggered sync is already in flight.",
                account.Id
            );
            return Result.Success(new ReconciliationOutcome(MessagesFound: 0, CursorWasSeeded: false, Skipped: true));
        }

        var (cursor, wasSeeded) = await ProviderSyncCursorSeeder.GetOrSeedAsync(
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

        return syncResult.IsFailure
            ? Result.Failure<ReconciliationOutcome>(syncResult.Error)
            : Result.Success(new ReconciliationOutcome(syncResult.Value, wasSeeded, Skipped: false));
    }
}
