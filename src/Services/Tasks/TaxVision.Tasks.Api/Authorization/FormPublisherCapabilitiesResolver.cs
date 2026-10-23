using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using TaxVision.DataCollection.Engine.Mapping;

namespace TaxVision.Tasks.Api.Authorization;

// Permisos efectivos del actor sobre lo que el catálogo de destinos exige; el engine solo ve este resultado.
public sealed class FormPublisherCapabilitiesResolver(
    IUserPermissionsSource permissions,
    IDataCollectionTargetCatalog targetCatalog
)
{
    public async Task<PublisherCapabilities> ResolveAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var granted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var permission in targetCatalog.GetSnapshot().ReferencedPermissions())
        {
            if (await permissions.HasPermissionAsync(user, permission, ct))
                granted.Add(permission);
        }
        return new PublisherCapabilities(
            granted,
            user.GetActorType() is ActorType.TenantAdmin or ActorType.PlatformAdmin
        );
    }

    public Task<bool> CanConfigureActionsAsync(ClaimsPrincipal user, CancellationToken ct) =>
        permissions.HasPermissionAsync(user, TasksPermissions.FormTemplatesConfigureActions, ct);
}
