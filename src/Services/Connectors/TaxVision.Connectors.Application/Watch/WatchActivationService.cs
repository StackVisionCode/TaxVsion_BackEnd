using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Domain.Watch;
using Wolverine;

namespace TaxVision.Connectors.Application.Watch;

/// <summary>
/// Lógica de activación de watch/subscription extraída de <see cref="SetupWatchHandler"/> para que los
/// flujos de connect (manual y OAuth) la llamen EN PROCESO — compartiendo la misma transacción/DbContext
/// que acaban de usar para persistir la cuenta — en vez de despachar <see cref="SetupWatchCommand"/> como
/// un mensaje nuevo de Wolverine. Un <c>bus.InvokeAsync</c> anidado abriría su propio scope/transacción,
/// y no vería la cuenta recién insertada en la transacción externa todavía sin commitear. El endpoint
/// standalone de reauth (<c>AccountsController.Reauth</c> → <see cref="SetupWatchHandler"/>) no tiene
/// este problema porque ahí SÍ es la operación de nivel superior — sigue yendo por
/// <c>SetupWatchCommand</c> vía Wolverine sin ningún cambio.
/// </summary>
public static class WatchActivationService
{
    public static async Task<Result> ActivateAsync(
        Guid tenantId,
        Guid accountId,
        ITenantEmailAccountRepository accountRepository,
        IProviderWatchSubscriptionRepository subscriptionRepository,
        IWatchProviderClientFactory watchClientFactory,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var accountResult = await accountRepository.GetByIdAsync(accountId, ct);
        if (accountResult.IsFailure)
            return Result.Failure(accountResult.Error);

        var account = accountResult.Value;
        if (account.TenantId != tenantId)
            return Result.Failure(
                // Código histórico (ErrorHttpMapping.cs ya lo mapea a 403) — se conserva aunque la
                // lógica se movió acá para que los callers (ErrorHttpMapping, frontend) no necesiten
                // ningún cambio.
                new Error("SetupWatchHandler.Forbidden", "Account does not belong to the caller's tenant.")
            );

        var now = DateTime.UtcNow;

        if (
            account.Status
            is TenantEmailAccountStatus.Draft
                or TenantEmailAccountStatus.Error
                or TenantEmailAccountStatus.Disconnected
        )
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

            await PublishConnectedAsync(account, now, bus, correlation);
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success();
        }

        var clientResult = watchClientFactory.Resolve(account.ProviderCode);
        if (clientResult.IsFailure)
            return Result.Failure(clientResult.Error);

        WatchSetupResult setup;
        try
        {
            setup = await clientResult.Value.SetupWatchAsync(accountId, ct);
        }
        catch (WatchProviderException ex)
        {
            // Código histórico (ErrorHttpMapping.cs ya lo mapea) — se conserva por el mismo motivo
            // que "SetupWatchHandler.Forbidden" arriba.
            return Result.Failure(new Error("SetupWatchHandler.ProviderFailed", ex.Message));
        }

        var subscriptionResult = await subscriptionRepository.GetByAccountIdAsync(accountId, ct);
        if (subscriptionResult.IsSuccess)
        {
            subscriptionResult.Value.Renew(setup.SubscriptionRef, setup.ExpiresAtUtc, now);
        }
        else
        {
            var createResult = ProviderWatchSubscription.Create(
                accountId,
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

        await PublishConnectedAsync(account, now, bus, correlation);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// Avisa a los demás servicios (Postmaster/Correspondence proyectan la cuenta como enviable) que la
    /// cuenta quedó activa. Se publica acá — el punto único de activación — para que TAMBIÉN el reauth
    /// (SetupWatchHandler) lo emita, no solo el connect OAuth inicial; antes, una cuenta recuperada por
    /// reauth quedaba activa en Connectors pero invisible para Postmaster → 403 al enviar. El consumer
    /// es idempotente (reconciliación por AccountId), así que re-emitirlo no duplica.
    /// </summary>
    private static ValueTask PublishConnectedAsync(
        TenantEmailAccount account,
        DateTime now,
        IMessageBus bus,
        ICorrelationContext correlation
    ) =>
        bus.PublishAsync(
            new ConnectorsTenantEmailAccountConnectedIntegrationEvent
            {
                CorrelationId = correlation.CorrelationId,
                TenantId = account.TenantId,
                AccountId = account.Id,
                EmailAddress = account.EmailAddress,
                ProviderCode = account.ProviderCode.ToString(),
                ConnectedAtUtc = now,
            }
        );
}
