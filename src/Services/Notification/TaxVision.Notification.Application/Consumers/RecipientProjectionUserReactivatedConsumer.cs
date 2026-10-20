using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Consumers;

public static class RecipientProjectionUserReactivatedConsumer
{
    public static async Task Handle(
        UserReactivatedIntegrationEvent evt,
        ITenantOwnerProjectionRepository owners,
        ICustomerPortalAccountProjectionRepository portalAccounts,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var owner = await owners.GetByUserAsync(evt.TenantId, evt.UserId, ct);
            owner?.MarkActive();

            var portal = await portalAccounts.GetByUserAsync(evt.TenantId, evt.UserId, ct);
            if (portal is not null)
                portal.MarkActive(evt.UserId);

            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
