using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Common;

/// <summary>
/// Traduce <c>userId → email</c> para los consumers que solo reciben el id del destinatario
/// (<c>reminder.due.v1</c> el primero). Local primero, Auth después.
///
/// <para>
/// <b>El pull no es un detalle opcional, es lo que hace usable la proyección.</b> Sin él la
/// proyección solo funcionaría para usuarios registrados <i>después</i> de que la tabla existiera:
/// un correo que anda solo para los usuarios nuevos es peor que no tener correo, porque parece que
/// funciona. Mismo patrón que <c>ProjectionPermissionsSource</c> con los permisos.
/// </para>
///
/// <para>
/// <b>Lo que trae el pull se persiste.</b> Un miss cuesta una llamada HTTP la primera vez y ninguna
/// después; si no se guardara, cada recordatorio de ese usuario volvería a pegarle a Auth.
/// </para>
/// </summary>
public sealed class UserEmailResolver(
    IUserEmailDirectoryRepository directory,
    IUserContactSnapshotClient snapshotClient,
    IUnitOfWork unitOfWork,
    ILogger<UserEmailResolver> logger
)
{
    /// <summary>Devuelve la dirección, o <c>null</c> si no hay a quién escribirle (o no corresponde).</summary>
    public async Task<string?> ResolveAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var local = await directory.FindAsync(tenantId, userId, ct);
        if (local is not null && local.IsUsable)
            return local.Email;

        var remote = await snapshotClient.FetchContactAsync(tenantId, userId, ct);
        if (remote is null)
        {
            logger.LogWarning(
                "No email available for user {UserId} in tenant {TenantId}: not in the local directory and "
                    + "the pull against Auth returned nothing. The email channel is skipped for this notification.",
                userId,
                tenantId
            );
            return null;
        }

        if (local is null)
            await directory.AddAsync(
                Domain.Directory.UserEmailDirectoryEntry.Create(tenantId, userId, remote.Email, remote.IsActive),
                ct
            );
        else
            local.UpdateEmail(remote.Email, remote.IsActive);

        await unitOfWork.SaveChangesAsync(ct);

        // Un usuario desactivado sí tiene dirección, pero no corresponde escribirle. Se persiste
        // igual para no repetir el pull en cada disparo posterior.
        return remote.IsActive ? remote.Email : null;
    }
}
