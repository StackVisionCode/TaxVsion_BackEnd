using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Permissions;

namespace TaxVision.Signature.Infrastructure.Permissions;

/// <summary>
/// H-04 — persiste el snapshot que trajo <see cref="IPermissionsSnapshotClient"/> para que la
/// PRÓXIMA request encuentre fila local. Wrapper angosto sobre el repositorio de proyección que ya
/// existía (RBAC Fase 7), con el mismo upsert idempotente que usan los consumers de Auth.
///
/// <para>
/// Corre dentro del pipeline HTTP de autorización, no en un consumer: dos requests concurrentes del
/// mismo usuario pueden chocar contra el índice único (TenantId, UserId). Ese conflicto se traga —
/// significa que el otro request ya persistió la misma fila, y ésta ya decidió con el snapshot
/// recién traído de Auth.
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
