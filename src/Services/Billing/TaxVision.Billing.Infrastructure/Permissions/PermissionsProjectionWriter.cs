using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Domain.Permissions;

namespace TaxVision.Billing.Infrastructure.Permissions;

/// <summary>
/// Opción B (recuperación pull bajo demanda) — wrapper angosto sobre el
/// <see cref="IAuthzUserPermissionsProjectionRepository"/> ya existente (RBAC Fase 7), con el mismo
/// upsert idempotente que los consumers de <c>UserRolesChangedIntegrationEvent</c>.
///
/// <para>
/// Corre DENTRO del pipeline de autorización HTTP, no en un consumer de Wolverine: dos requests
/// concurrentes del mismo usuario pueden intentar el mismo insert bajo el índice único
/// (TenantId, UserId). Se traga ese conflicto en vez de romper una request que sí tenía el permiso
/// correcto — <c>ProjectionPermissionsSource</c> ya decidió con el snapshot recién traído de Auth,
/// y persistir es solo para que la PRÓXIMA request encuentre fila.
/// </para>
/// </summary>
internal sealed class PermissionsProjectionWriter(
    IAuthzUserPermissionsProjectionRepository repository,
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
                var projection = AuthzUserPermissionsProjection.Create(
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
