using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Domain.Accounts;
using Wolverine;

namespace TaxVision.Connectors.Application.Sync;

public static class StartMailboxResyncHandler
{
    public const string AccountNotActiveCode = "MailboxResync.AccountNotActive";

    public static async Task<Result> Handle(
        StartMailboxResyncCommand cmd,
        ITenantEmailAccountRepository accountRepository,
        MailboxResyncLimits limits,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger logger,
        CancellationToken ct
    )
    {
        if (cmd.FromUtc >= cmd.ToUtc)
            return Result.Failure(new Error("MailboxResync.InvalidWindow", "fromUtc must be earlier than toUtc."));
        if (cmd.ToUtc - cmd.FromUtc > TimeSpan.FromDays(limits.MaxWindowDays))
            return Result.Failure(
                new Error("MailboxResync.WindowTooLarge", $"The window cannot exceed {limits.MaxWindowDays} days.")
            );

        var accountResult = await accountRepository.GetByIdAsync(cmd.AccountId, ct);
        if (accountResult.IsFailure || accountResult.Value.TenantId != cmd.TenantId)
            return Result.Failure(new Error("TenantEmailAccount.NotFound", "The email account was not found."));

        var account = accountResult.Value;
        if (account.Status is not (TenantEmailAccountStatus.Active or TenantEmailAccountStatus.Connected))
            return Result.Failure(new Error(AccountNotActiveCode, $"The email account is {account.Status}."));

        // Todas las páginas de la corrida comparten este id: se siguen juntas en los logs.
        var runId = string.IsNullOrEmpty(correlation.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : correlation.CorrelationId;
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(new ResyncMailboxCommand(cmd.TenantId, account.Id, cmd.FromUtc, cmd.ToUtc, null, runId));

        logger.LogInformation(
            "Mailbox resync {RunId} queued for account {AccountId} ({Provider}) from {FromUtc} to {ToUtc}.",
            runId,
            account.Id,
            account.ProviderCode,
            cmd.FromUtc,
            cmd.ToUtc
        );
        return Result.Success();
    }
}
