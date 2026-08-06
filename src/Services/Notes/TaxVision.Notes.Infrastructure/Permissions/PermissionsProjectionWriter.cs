using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaxVision.Notes.Application.Permissions.Abstractions;
using TaxVision.Notes.Domain.Permissions;

namespace TaxVision.Notes.Infrastructure.Permissions;

// ---------------------------------------------------------------------------
// Opción B (recuperación pull bajo demanda) — wrapper angosto sobre el IUserPermissionsProjectionRepository
// ya existente (RBAC Fase 7), mismo upsert idempotente que UserRolesChangedPermissionsProjectionConsumer.
// Corre DENTRO del pipeline de autorización HTTP (no en un consumer Wolverine) — dos requests
// concurrentes pueden intentar el mismo insert bajo el índice único (TenantId,UserId); se traga el
// conflicto (otro request ya ganó la carrera y persistió la misma fila) en vez de romper el request
// que sí tenía el permiso correcto — ProjectionPermissionsSource ya usa el snapshot recién traído de
// Auth para decidir esta request, la persistencia es solo para que la PRÓXIMA request encuentre fila.
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
            // Carrera contra otro request concurrente para el mismo usuario (índice único
            // TenantId+UserId) — la próxima lectura ya va a encontrar la fila que el otro request
            // persistió; no hace falta reintentar ni propagar, esta request ya decidió con el
            // snapshot recién traído de Auth.
            logger.LogInformation(
                ex,
                "Permissions projection write raced for user {UserId} in tenant {TenantId} — another request already persisted it.",
                userId,
                tenantId
            );
        }
    }
}
