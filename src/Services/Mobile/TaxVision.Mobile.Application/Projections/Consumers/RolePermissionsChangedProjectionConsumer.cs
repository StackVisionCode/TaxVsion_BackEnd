using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Mobile.Application.Projections.Abstractions;
using TaxVision.Mobile.Domain.Projections;

namespace TaxVision.Mobile.Application.Projections.Consumers;

// Auth manda a cada usuario afectado su UserRolesChanged ya sin los denies; este evento nunca suma permisos.
public static class RolePermissionsChangedProjectionConsumer
{
    public static async Task Handle(
        RolePermissionsChangedIntegrationEvent evt,
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
            var row = await projections.FindRolePermissionsAsync(evt.TenantId, evt.RoleId, ct);
            if (row is null)
                projections.AddRolePermissions(
                    RolePermissionsProjection.Create(
                        evt.TenantId,
                        evt.RoleId,
                        evt.RoleName,
                        evt.PermissionsVersion,
                        evt.PermissionCodes
                    )
                );
            else
                row.ApplyIfNewer(evt.RoleName, evt.PermissionsVersion, evt.PermissionCodes);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
