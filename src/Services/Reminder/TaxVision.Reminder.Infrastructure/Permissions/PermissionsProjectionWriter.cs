using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Reminder.Application.Permissions.Abstractions;
using TaxVision.Reminder.Domain.Permissions;

namespace TaxVision.Reminder.Infrastructure.Permissions;

/// <summary>
/// Persiste el snapshot que trajo <see cref="PermissionsSnapshotClient"/> para que la PRÓXIMA
/// request encuentre fila local; la request actual ya decidió con el snapshot en mano. Mismo upsert
/// idempotente que <c>UserRolesChangedPermissionsProjectionConsumer</c>.
///
/// <para>
/// Corre dentro del pipeline de autorización HTTP, no en un consumer: dos requests concurrentes del
/// mismo usuario pueden intentar el mismo insert bajo el índice único (TenantId, UserId). El
/// conflicto se traga — el otro request ya persistió la misma fila — en vez de romper una request
/// que sí tenía el permiso correcto.
/// </para>
///
/// <para>
/// Se atrapa <see cref="ConflictException"/>, NO <c>DbUpdateException</c>: <c>ReminderDbContext</c>
/// traduce <c>SqlException</c> 2601/2627 a <c>ConflictException</c>, que no hereda de
/// <c>DbUpdateException</c>. Los writers equivalentes de Billing, CloudStorage, Customer, Notes y
/// Signature atrapan <c>DbUpdateException</c> y por eso su catch nunca se ejecuta.
/// </para>
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
        catch (ConflictException ex)
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
