using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Api.Authorization;

/// <summary>
/// Buzones visibles: los personales del usuario y, con office.read (o write), los de oficina; nunca el personal de
/// otro empleado. Los lee de la proyección local y, si el tenant aún no tiene filas, de Connectors. Fail-closed: si no
/// hay userId o Connectors no responde, devuelve un set VACÍO. Vive en la capa Api porque depende del
/// <see cref="ClaimsPrincipal"/> y de <see cref="IUserPermissionsSource"/> (BuildingBlocks.Web).
/// </summary>
public sealed class MailboxVisibilityResolver(
    IUserPermissionsSource permissions,
    IConnectorsClient connectorsClient,
    IMailboxOwnershipRepository ownership,
    ILogger<MailboxVisibilityResolver> logger
) : IMailboxVisibilityResolver
{
    private static readonly IReadOnlyCollection<Guid> None = [];

    public async Task<IReadOnlyCollection<Guid>?> ResolveVisibleAccountIdsAsync(
        ClaimsPrincipal user,
        Guid tenantId,
        CancellationToken ct = default
    )
    {
        var canSeeOffice =
            await permissions.HasPermissionAsync(user, ConnectorsPermissions.AccountsOfficeRead, ct)
            || await permissions.HasPermissionAsync(user, ConnectorsPermissions.AccountsWrite, ct);
        if (!user.TryGetUserId(out var userId))
            return None;

        var owned = await ownership.ListOwnedAccountIdsAsync(tenantId, userId, ct);
        if (owned is not null)
            return canSeeOffice ? [.. owned.Union(await ownership.ListOfficeAccountIdsAsync(tenantId, ct))] : owned;

        var visible = await connectorsClient.GetVisibleAccountIdsAsync(tenantId, userId, canSeeOffice, ct);
        if (visible.IsFailure)
        {
            logger.LogWarning(
                "Could not resolve visible mailboxes for user {UserId}; hiding all mail (fail-closed): {Error}",
                userId,
                visible.Error.Code
            );
            return None;
        }

        return visible.Value;
    }
}
