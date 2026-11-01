using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Api.Authorization;

/// <summary>
/// Mismo criterio que Tasks, Notes y CloudStorage: con <c>customers.view_all</c> se ve todo; sin él, solo los
/// clientes asignados. Fail-closed: sin userId se acota a un id vacío, que no tiene clientes asignados.
/// </summary>
internal sealed class CustomerAccessResolver(IUserPermissionsSource permissions) : ICustomerAccessResolver
{
    public async Task<Guid?> ResolveRestrictedUserIdAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (await permissions.HasPermissionAsync(user, CustomersPermissions.ViewAll, ct))
            return null;
        return user.TryGetUserId(out var userId) ? userId : Guid.Empty;
    }
}
