using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using TaxVision.CloudStorage.Api.Controllers;

namespace TaxVision.CloudStorage.Tests.Architecture;

/// <summary>
/// Customer (archivo de una importación ya procesada) y Tenant (logo reemplazado) borran como servicio los
/// archivos que ellos mismos subieron. El borrado sigue exigiendo el permiso <c>cloudstorage.file.delete</c>.
/// </summary>
public sealed class ServiceFileDeletionTests
{
    [Fact]
    public void A_service_can_delete_a_file()
    {
        var action = typeof(FilesController).GetMethod(nameof(FilesController.Delete))!;

        Assert.Contains(ActorType.Service, action.GetCustomAttribute<AllowActorTypesAttribute>()!.ActorTypes);
        Assert.NotNull(action.GetCustomAttribute<HasPermissionAttribute>());
    }
}
