using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Domain.Watch;

namespace TaxVision.Connectors.Application.Watch;

public static class SetupWatchHandler
{
    public static async Task<Result> Handle(
        SetupWatchCommand cmd,
        ITenantEmailAccountRepository accountRepository,
        IProviderWatchSubscriptionRepository subscriptionRepository,
        IWatchProviderClientFactory watchClientFactory,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var accountResult = await accountRepository.GetByIdAsync(cmd.AccountId, ct);
        if (accountResult.IsFailure)
            return Result.Failure(accountResult.Error);

        var account = accountResult.Value;
        if (account.TenantId != cmd.TenantId)
            return Result.Failure(
                new Error("SetupWatchHandler.Forbidden", "Account does not belong to the caller's tenant.")
            );

        var now = DateTime.UtcNow;

        if (account.Status is TenantEmailAccountStatus.Draft or TenantEmailAccountStatus.Error)
        {
            var connectResult = account.MarkConnected(now);
            if (connectResult.IsFailure)
                return connectResult;
        }

        // IMAP no tiene mecanismo de push genérico — sin ProviderWatchSubscription que crear,
        // la cuenta pasa a Active directo (ver WatchProviderClientFactory).
        if (account.ProviderCode == ProviderCode.Imap)
        {
            var activateImapResult = account.Activate(now);
            if (activateImapResult.IsFailure)
                return activateImapResult;

            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success();
        }

        var clientResult = watchClientFactory.Resolve(account.ProviderCode);
        if (clientResult.IsFailure)
            return Result.Failure(clientResult.Error);

        WatchSetupResult setup;
        try
        {
            setup = await clientResult.Value.SetupWatchAsync(cmd.AccountId, ct);
        }
        catch (WatchProviderException ex)
        {
            return Result.Failure(new Error("SetupWatchHandler.ProviderFailed", ex.Message));
        }

        var subscriptionResult = await subscriptionRepository.GetByAccountIdAsync(cmd.AccountId, ct);
        if (subscriptionResult.IsSuccess)
        {
            subscriptionResult.Value.Renew(setup.SubscriptionRef, setup.ExpiresAtUtc, now);
        }
        else
        {
            var createResult = ProviderWatchSubscription.Create(
                cmd.AccountId,
                account.ProviderCode,
                setup.SubscriptionRef,
                setup.TopicName,
                setup.ExpiresAtUtc,
                now
            );
            if (createResult.IsFailure)
                return Result.Failure(createResult.Error);

            await subscriptionRepository.AddAsync(createResult.Value, ct);
        }

        var activateResult = account.Activate(now);
        if (activateResult.IsFailure)
            return activateResult;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
