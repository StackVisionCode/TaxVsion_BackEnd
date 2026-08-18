using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Reminder.Application.Permissions.Abstractions;
using TaxVision.Reminder.Domain.Permissions;

namespace TaxVision.Reminder.Application.Permissions.Consumers;

// RBAC Fase 7 — mantiene la proyección local de permisos que consulta ProjectionPermissionsSource
// (BuildingBlocks.Web) para enforzar perm_v sin llamar a Auth por HTTP en el hot path de
// autorización. Copiado del shape de Notes/CloudStorage/Signature a propósito, no improvisado: los
// dos bugs reales que ya costó este consumer fueron (a) un upsert que no comparaba
// PermissionsVersion y dejaba usuarios fail-closed en silencio sin DLQ, y (b) casing
// camelCase/PascalCase al leer el evento.

public static class UserRolesChangedPermissionsProjectionConsumer
{
    public static async Task Handle(
        UserRolesChangedIntegrationEvent evt,
        IUserPermissionsProjectionRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<UserPermissionsProjection> logger,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            var existing = await repository.GetAsync(evt.TenantId, evt.UserId, ct);
            if (existing is null)
            {
                var projection = UserPermissionsProjection.Create(
                    evt.TenantId,
                    evt.UserId,
                    evt.PermissionsVersion,
                    evt.PermissionCodes,
                    evt.RoleIds
                );
                await repository.AddAsync(projection, ct);
                logger.LogInformation(
                    "UserPermissionsProjection created for {UserId} version {Version}.",
                    evt.UserId,
                    evt.PermissionsVersion
                );
            }
            else
            {
                existing.ApplyIfNewer(evt.PermissionsVersion, evt.PermissionCodes, evt.RoleIds);
            }
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
