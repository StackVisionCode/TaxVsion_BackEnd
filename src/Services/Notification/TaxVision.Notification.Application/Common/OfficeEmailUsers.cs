using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Common;

/// <summary>
/// Owner o admin activo dueño de un correo que el productor manda sin id de usuario. Con el id, el correo pasa por
/// el gate (inactivo y preferencia) como el resto de correos al staff.
/// </summary>
public static class OfficeEmailUsers
{
    public static async Task<Guid?> FindAsync(
        Guid tenantId,
        string email,
        ITenantOwnerProjectionRepository offices,
        IUserEmailDirectoryRepository directory,
        CancellationToken ct
    )
    {
        var address = email.Trim();
        var owner = await offices.GetActiveOwnerUserIdAsync(tenantId, ct);
        var admins = await offices.GetActiveAdminUserIdsAsync(tenantId, ct);
        foreach (var userId in admins.Prepend(owner ?? Guid.Empty).Where(id => id != Guid.Empty).Distinct())
        {
            var entry = await directory.FindAsync(tenantId, userId, ct);
            if (entry is not null && string.Equals(entry.Email.Trim(), address, StringComparison.OrdinalIgnoreCase))
                return userId;
        }
        return null;
    }
}
