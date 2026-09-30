using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using TaxVision.CloudStorage.Api.Controllers;

namespace TaxVision.CloudStorage.Tests.Api;

/// <summary>
/// Navegar carpetas es lo único que el cliente hace acá, y el permiso que lo dice es
/// <c>portal.folders.view</c>. Vive en su rol de portal desde siempre; sin este atributo era
/// decorativo —quitárselo no cambiaba nada— porque el endpoint solo miraba el permiso del staff.
/// </summary>
public sealed class FoldersControllerPortalPermissionTests
{
    [Theory]
    [InlineData(nameof(FoldersController.Contents))]
    [InlineData(nameof(FoldersController.Tree))]
    public void Portal_clients_need_the_folders_permission_to_browse(string action)
    {
        var attribute = typeof(FoldersController)
            .GetMethod(action, BindingFlags.Public | BindingFlags.Instance)!
            .GetCustomAttributes<HasPermissionForActorAttribute>()
            .SingleOrDefault(candidate => candidate.ActorType == ActorType.CustomerPortal);

        Assert.NotNull(attribute);
        Assert.Equal(PortalPermissions.FoldersView, attribute.Permission);
    }
}
