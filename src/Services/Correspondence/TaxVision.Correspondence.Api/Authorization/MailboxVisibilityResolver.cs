using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Api.Authorization;

/// <summary>
/// Con office.read (o write) el usuario ve todo → <c>null</c> (el hot path no filtra). Sin él, pide a
/// Connectors sus buzones visibles (solo los personales). Fail-closed: si no hay userId o Connectors no
/// responde, devuelve un set VACÍO (no ve nada) en vez de abrir el buzón de oficina. Vive en la capa Api
/// porque depende del <see cref="ClaimsPrincipal"/> y de <see cref="IUserPermissionsSource"/> (BuildingBlocks.Web).
/// </summary>
internal sealed class MailboxVisibilityResolver(
    IUserPermissionsSource permissions,
    IConnectorsClient connectorsClient,
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
        if (canSeeOffice)
            return null;

        if (!user.TryGetUserId(out var userId))
            return None;

        var visible = await connectorsClient.GetVisibleAccountIdsAsync(tenantId, userId, includeOffice: false, ct);
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
