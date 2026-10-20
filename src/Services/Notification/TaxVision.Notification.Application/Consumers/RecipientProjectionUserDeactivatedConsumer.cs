using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Consumers;

public static class RecipientProjectionUserDeactivatedConsumer
{
    public static async Task Handle(
        UserDeactivatedIntegrationEvent evt,
        ITenantOwnerProjectionRepository owners,
        ICustomerPortalAccountProjectionRepository portalAccounts,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            await DeactivateAsync(owners, portalAccounts, evt.TenantId, evt.UserId, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    internal static async Task DeactivateAsync(
        ITenantOwnerProjectionRepository owners,
        ICustomerPortalAccountProjectionRepository portalAccounts,
        Guid tenantId,
        Guid userId,
        CancellationToken ct
    )
    {
        var owner = await owners.GetByUserAsync(tenantId, userId, ct);
        owner?.MarkInactive();

        var portal = await portalAccounts.GetByUserAsync(tenantId, userId, ct);
        portal?.MarkInactive();
    }
}
