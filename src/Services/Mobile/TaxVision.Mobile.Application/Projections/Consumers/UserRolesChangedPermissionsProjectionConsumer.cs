using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Mobile.Application.Projections.Abstractions;
using TaxVision.Mobile.Domain.Projections;

namespace TaxVision.Mobile.Application.Projections.Consumers;

public static class UserRolesChangedPermissionsProjectionConsumer
{
    public static async Task Handle(
        UserRolesChangedIntegrationEvent evt,
        IMobileProjectionRepository projections,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            var row = await projections.FindUserPermissionsAsync(evt.TenantId, evt.UserId, ct);
            if (row is null)
                projections.AddUserPermissions(
                    UserPermissionsProjection.Create(
                        evt.TenantId,
                        evt.UserId,
                        evt.PermissionsVersion,
                        evt.PermissionCodes,
                        evt.RoleIds,
                        evt.OccurredOn
                    )
                );
            else
                row.ApplyIfNewer(evt.PermissionsVersion, evt.PermissionCodes, evt.RoleIds, evt.OccurredOn);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
