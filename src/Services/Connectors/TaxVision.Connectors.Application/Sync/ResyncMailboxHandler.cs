using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Domain.Accounts;
using Wolverine;

namespace TaxVision.Connectors.Application.Sync;

/// <summary>
/// Re-sincroniza una página del INBOX por fecha y publica cada mensaje con <c>IsReplay = true</c>.
/// No toca el cursor ni revisa avisos de no entrega. Correspondence descarta lo que ya tiene (COR-09).
/// </summary>
public static class ResyncMailboxHandler
{
    /// <returns>La página siguiente (cascada a la cola local) o null si la cadena termina.</returns>
    public static async Task<ResyncMailboxCommand?> Handle(
        ResyncMailboxCommand cmd,
        ITenantEmailAccountRepository accountRepository,
        IEmailProviderClientFactory emailClientFactory,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger logger,
        CancellationToken ct
    )
    {
        var accountResult = await accountRepository.GetByIdAsync(cmd.AccountId, ct);
        if (accountResult.IsFailure || accountResult.Value.TenantId != cmd.TenantId)
        {
            logger.LogWarning("Mailbox resync for account {AccountId} stopped: account not found.", cmd.AccountId);
            return null;
        }

        // Sin refrescar tokens de cuentas en Error/Disconnected: así no puede generar refresh_failed.
        var account = accountResult.Value;
        if (account.Status is not (TenantEmailAccountStatus.Active or TenantEmailAccountStatus.Connected))
        {
            logger.LogWarning(
                "Mailbox resync for account {AccountId} stopped: account is {Status}.",
                account.Id,
                account.Status
            );
            return null;
        }

        var clientResult = emailClientFactory.Resolve(account.ProviderCode);
        if (clientResult.IsFailure)
        {
            logger.LogWarning(
                "Mailbox resync for account {AccountId} stopped: {Error}.",
                account.Id,
                clientResult.Error.Code
            );
            return null;
        }

        var client = clientResult.Value;
        InboxMessageIdPage page;
        IReadOnlyList<ProviderMessageFetch> fetched;
        try
        {
            page = await client.ListInboxMessageIdsAsync(
                account.Id,
                account.TenantId,
                cmd.FromUtc,
                cmd.ToUtc,
                cmd.PageToken,
                ct
            );
            fetched =
                page.MessageIds.Count > 0
                    ? await client.GetMessagesAsync(account.Id, account.TenantId, page.MessageIds, ct)
                    : [];
        }
        catch (EmailProviderException ex) when (ex.RequiresReconnect)
        {
            return StopForReconnect(account, logger);
        }

        // Un fallo transitorio repite la página entera (Wolverine reintenta; agotado, DLQ). Correspondence deduplica.
        var failed = fetched.FirstOrDefault(f => f.Message is null && !f.IsNotFound);
        if (failed?.Error is { } error)
        {
            if (error.RequiresReconnect)
                return StopForReconnect(account, logger);
            throw error;
        }

        var published = 0;
        foreach (var item in fetched)
        {
            if (item.Message is null)
                continue;

            await bus.PublishAsync(
                RawMessageSyncOrchestrator.BuildEvent(account, item.Message, cmd.CorrelationId, isReplay: true)
            );
            published++;
        }

        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation(
            "Mailbox resync {RunId} for account {AccountId}: republished {Published}/{Listed} message(s); more pages: {HasMore}.",
            cmd.CorrelationId,
            account.Id,
            published,
            page.MessageIds.Count,
            page.NextPageToken is not null
        );

        return page.NextPageToken is null ? null : cmd with { PageToken = page.NextPageToken };
    }

    private static ResyncMailboxCommand? StopForReconnect(TenantEmailAccount account, ILogger logger)
    {
        logger.LogWarning(
            "Mailbox resync for account {AccountId} stopped: the account needs to be reconnected.",
            account.Id
        );
        return null;
    }
}
