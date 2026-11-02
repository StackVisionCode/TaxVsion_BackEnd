using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using TaxVision.CloudStorage.Api.Controllers;

namespace TaxVision.CloudStorage.Tests.Architecture;

/// <summary>
/// Signature entrega el firmado con su propio enlace y, cuando firman varios clientes, deja una copia en el
/// portal de cada uno: puede COPIAR archivos como servicio, pero compartir sigue siendo solo de personas.
/// </summary>
public sealed class SignatureServiceFileAccessTests
{
    [Fact]
    public void A_service_can_copy_a_file_to_a_customers_folder()
    {
        Assert.Contains(ActorType.Service, ActorTypesOf(typeof(FilesController), nameof(FilesController.Copy)));
    }

    [Fact]
    public void Only_people_can_create_share_links()
    {
        Assert.DoesNotContain(ActorType.Service, ActorTypesOf(typeof(ShareLinksController), nameof(ShareLinksController.Create)));
    }

    private static IReadOnlyCollection<ActorType> ActorTypesOf(Type controller, string action) =>
        controller.GetMethod(action)!.GetCustomAttribute<AllowActorTypesAttribute>()!.ActorTypes;
}
