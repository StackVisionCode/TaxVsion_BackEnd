using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Consumers;

public static class RecipientProjectionUserOffboardedConsumer
{
    public static async Task Handle(
        UserOffboardedIntegrationEvent evt,
        ITenantOwnerProjectionRepository owners,
        ICustomerPortalAccountProjectionRepository portalAccounts,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            await RecipientProjectionUserDeactivatedConsumer.DeactivateAsync(
                owners,
                portalAccounts,
                evt.TenantId,
                evt.UserId,
                ct
            );
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
