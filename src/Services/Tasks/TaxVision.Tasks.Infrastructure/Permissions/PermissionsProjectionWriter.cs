using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.Application.Permissions.Abstractions;
using TaxVision.Tasks.Domain.Permissions;

namespace TaxVision.Tasks.Infrastructure.Permissions;

/// <summary>
/// Persiste el snapshot que <c>ProjectionPermissionsSource</c> acaba de traer de Auth cuando no
/// encontró fila local. Corre dentro del pipeline HTTP, no en un consumer: dos requests concurrentes
/// del mismo usuario pueden chocar contra el índice único (TenantId, UserId). Ese choque se traga —
/// el otro request ya persistió la misma fila, y esta request ya decidió con el snapshot en mano.
/// </summary>
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
