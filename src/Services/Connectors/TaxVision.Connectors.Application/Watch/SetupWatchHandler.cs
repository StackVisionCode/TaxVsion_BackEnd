using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Accounts;
using Wolverine;

namespace TaxVision.Connectors.Application.Watch;

/// <summary>
/// Entry point de Wolverine para el reauth manual (<c>AccountsController.Reauth</c>) — ahí SÍ es la
/// operación de nivel superior, así que despachar <see cref="SetupWatchCommand"/> como mensaje es
/// correcto. Los flujos de connect (manual y OAuth) NO pasan por acá — llaman
/// <see cref="WatchActivationService.ActivateAsync"/> directo para compartir su propia transacción
/// (ver docblock de esa clase).
/// </summary>
public static class SetupWatchHandler
{
    public static Task<Result> Handle(
        SetupWatchCommand cmd,
        ITenantEmailAccountRepository accountRepository,
        IProviderWatchSubscriptionRepository subscriptionRepository,
        IWatchProviderClientFactory watchClientFactory,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        WatchActivationService.ActivateAsync(
            cmd.TenantId,
            cmd.AccountId,
            accountRepository,
            subscriptionRepository,
            watchClientFactory,
            unitOfWork,
            bus,
            correlation,
            ct
        );
}
