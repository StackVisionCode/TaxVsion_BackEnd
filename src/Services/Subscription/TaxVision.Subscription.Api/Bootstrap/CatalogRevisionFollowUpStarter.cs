using BuildingBlocks.Infrastructure.Hosting;
using TaxVision.Subscription.Application.Catalog.Revisions;
using Wolverine;

namespace TaxVision.Subscription.Api.Bootstrap;

/// <summary>
/// Ya con Wolverine arrancado, manda los avisos de las revisiones del catálogo aplicadas antes de <c>RunAsync</c>.
/// El handler es transaccional con outbox: si el proceso cae, el próximo arranque los manda.
/// </summary>
public sealed class CatalogRevisionFollowUpStarter(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<CatalogRevisionFollowUpStarter> logger
) : DeferredStartupHostedService(lifetime, logger)
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync(new PublishCatalogRevisionFollowUpsCommand(), cancellationToken);
    }
}
