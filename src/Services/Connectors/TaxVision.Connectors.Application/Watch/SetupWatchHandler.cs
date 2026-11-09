using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Domain.Accounts;
using Wolverine.Attributes;

namespace TaxVision.Connectors.Application.Watch;

/// <summary>
/// Entry point de Wolverine para el reauth manual (<c>AccountsController.Reauth</c>) — ahí SÍ es la
/// operación de nivel superior, así que despachar <see cref="SetupWatchCommand"/> como mensaje es
/// correcto. Los flujos de connect (manual y OAuth) NO pasan por acá — llaman
/// <see cref="WatchActivationService.ActivateAsync"/> directo (ver docblock de esa clase). Sin grant vigente no hay
/// reauth posible: responde que hay que reconectar, sin tocar la cuenta ni intentar el refresh.
/// </summary>
public static class SetupWatchHandler
{
    public const string ReconnectRequiredCode = "Connectors.ReconnectRequired";

    [NonTransactional]
    public static async Task<Result> Handle(
        SetupWatchCommand cmd,
        ITenantEmailAccountRepository accountRepository,
        IOAuthConnectionRepository connectionRepository,
        IProviderWatchSubscriptionRepository subscriptionRepository,
        IWatchProviderClientFactory watchClientFactory,
        IOutboxUnit unit,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var connection = await connectionRepository.GetByAccountIdReadOnlyAsync(cmd.AccountId, ct);
        if (
            connection.IsSuccess
            && connection.Value.Status is OAuthConnectionStatus.Expired or OAuthConnectionStatus.Revoked
        )
            return Result.Failure(
                new Error(ReconnectRequiredCode, "The mailbox authorization is no longer valid. Reconnect the account.")
            );

        return await WatchActivationService.ActivateAsync(
            cmd.TenantId,
            cmd.AccountId,
            accountRepository,
            subscriptionRepository,
            watchClientFactory,
            unit,
            correlation,
            ct
        );
    }
}
