using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Domain.Watch;
using Wolverine;

namespace TaxVision.Connectors.Application.Watch;

/// <summary>
/// Lógica de activación de watch/subscription extraída de <see cref="SetupWatchHandler"/> para que los
/// flujos de connect (manual y OAuth) la llamen EN PROCESO, sobre el mismo DbContext que acaban de usar para
/// persistir la cuenta. La llamada al proveedor va sin transacción (CON-03) y el resultado se confirma en una
/// unidad corta junto con el aviso. El conectar manual es IMAP, no llama a nadie, y usa la unidad de su handler.
/// </summary>
public static class WatchActivationService
{
    public static async Task<Result> ActivateAsync(
        Guid tenantId,
        Guid accountId,
        ITenantEmailAccountRepository accountRepository,
        IProviderWatchSubscriptionRepository subscriptionRepository,
        IWatchProviderClientFactory watchClientFactory,
        IOutboxUnit unit,
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
        var outcome = Result.Success();

        // IMAP no tiene mecanismo de push genérico — sin ProviderWatchSubscription que crear,
        // la cuenta pasa a Active directo (ver WatchProviderClientFactory).
        if (account.ProviderCode == ProviderCode.Imap)
        {
            await unit.RunAsync(
                async (outbox, _) =>
                {
                    outcome = ConnectAndActivate(account, now);
                    if (outcome.IsFailure)
                        return false;

                    await PublishConnectedAsync(account, now, outbox, correlation);
                    return true;
                },
                ct
            );
            return outcome;
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

        await unit.RunAsync(
            async (outbox, token) =>
            {
                var subscriptionResult = await subscriptionRepository.GetByAccountIdAsync(accountId, token);
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
                    {
                        outcome = Result.Failure(createResult.Error);
                        return false;
                    }

                    await subscriptionRepository.AddAsync(createResult.Value, token);
                }

                outcome = ConnectAndActivate(account, now);
                if (outcome.IsFailure)
                    return false;

                await PublishConnectedAsync(account, now, outbox, correlation);
                return true;
            },
            ct
        );
        return outcome;
    }

    private static Result ConnectAndActivate(TenantEmailAccount account, DateTime now)
    {
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

        return account.Activate(now);
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
                OwnerUserId = account.OwnerUserId,
                IsOffice = account.IsOffice,
            }
        );
}
