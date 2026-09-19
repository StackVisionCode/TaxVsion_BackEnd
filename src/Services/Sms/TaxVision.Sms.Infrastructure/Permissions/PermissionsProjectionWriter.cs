using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaxVision.Sms.Application.Permissions.Abstractions;
using TaxVision.Sms.Domain.Permissions;

namespace TaxVision.Sms.Infrastructure.Permissions;

// ---------------------------------------------------------------------------
// Opción B (recuperación pull bajo demanda) — wrapper angosto sobre el IUserPermissionsProjectionRepository
// ya existente (RBAC), mismo upsert idempotente que el consumer de UserRolesChanged. Corre DENTRO del
// pipeline de autorización HTTP (no en un consumer Wolverine) — dos requests concurrentes pueden
// intentar el mismo insert bajo el índice único (TenantId,UserId); se traga el conflicto en vez de
// romper el request que sí tenía el permiso correcto. La persistencia es solo para que la PRÓXIMA
// request encuentre fila; esta ya decidió con el snapshot recién traído de Auth.
// ---------------------------------------------------------------------------

internal sealed class PermissionsProjectionWriter(
    IUserPermissionsProjectionRepository repository,
    IUnitOfWork unitOfWork,
    ILogger<PermissionsProjectionWriter> logger
) : IUserPermissionsProjectionWriter
{
    public async Task PersistSnapshotAsync(
        Guid tenantId,
        Guid userId,
        RemotePermissionsSnapshot snapshot,
        CancellationToken ct = default
    )
    {
        try
        {
            var existing = await repository.GetAsync(tenantId, userId, ct);
            if (existing is null)
            {
                var projection = UserPermissionsProjection.Create(
                    tenantId,
                    userId,
                    snapshot.PermissionsVersion,
                    snapshot.PermissionCodes,
                    snapshot.RoleIds
                );
                await repository.AddAsync(projection, ct);
            }
            else
            {
                existing.ApplyIfNewer(snapshot.PermissionsVersion, snapshot.PermissionCodes, snapshot.RoleIds);
            }
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogInformation(
                ex,
                "Permissions projection write raced for user {UserId} in tenant {TenantId} — another request already persisted it.",
                userId,
                tenantId
            );
        }
    }
}
