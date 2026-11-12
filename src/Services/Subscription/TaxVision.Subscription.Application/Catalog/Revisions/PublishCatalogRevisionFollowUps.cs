using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlementsForAllPlans;
using Wolverine;

namespace TaxVision.Subscription.Application.Catalog.Revisions;

/// <summary>Manda los avisos de las revisiones aplicadas al desplegar. Lo dispara el arranque, ya con Wolverine.</summary>
public sealed record PublishCatalogRevisionFollowUpsCommand;

/// <summary>
/// Las revisiones se aplican antes de <c>RunAsync</c>, cuando Wolverine todavía no puede publicar. Sus avisos salen
/// acá, por el outbox y una sola vez: un recálculo de todas las oficinas (toma la edición re-anclada) y un
/// <c>CatalogChanged</c> por revisión. La marca y los avisos se confirman juntos.
/// </summary>
public static class PublishCatalogRevisionFollowUpsHandler
{
    public static async Task Handle(
        PublishCatalogRevisionFollowUpsCommand command,
        ICatalogRevisionStore store,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var pending = await store.GetPendingFollowUpsAsync(ct);
        if (pending.Count == 0)
            return;

        var nowUtc = DateTime.UtcNow;
        foreach (var revision in pending)
            revision.MarkFollowUpPublished(nowUtc);
        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(new RecalculateEntitlementsForAllPlansCommand());
        foreach (var revision in pending)
            await bus.PublishAsync(
                new CatalogChangedIntegrationEvent
                {
                    ChangeKind = "catalog.revision",
                    EntityCode = revision.RevisionId,
                    CorrelationId = string.Empty,
                }
            );
    }
}
