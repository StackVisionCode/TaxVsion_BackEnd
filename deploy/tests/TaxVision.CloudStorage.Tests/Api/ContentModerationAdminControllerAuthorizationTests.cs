using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using TaxVision.CloudStorage.Api.Controllers;

namespace TaxVision.CloudStorage.Tests.Api;

/// <summary>
/// La moderación de contenido es de la plataforma (T-20): ninguna acción puede quedar abierta a un actor
/// de oficina ni sin el permiso PlatformOnly, ni siquiera por un override a nivel de método.
/// </summary>
public sealed class ContentModerationAdminControllerAuthorizationTests
{
    private static readonly Type Controller = typeof(ContentModerationAdminController);
    private static readonly string ExpectedPolicy =
        HasPermissionAttribute.PolicyPrefix + CloudStoragePermissions.ModerationManage;

    private static IEnumerable<MethodInfo> Actions() =>
        Controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes().OfType<HttpMethodAttribute>().Any());

    [Fact]
    public void Controller_only_admits_PlatformAdmin()
    {
        var allowed = Controller.GetCustomAttribute<AllowActorTypesAttribute>();

        Assert.NotNull(allowed);
        Assert.Equal([ActorType.PlatformAdmin], allowed.ActorTypes);
    }

    [Fact]
    public void Controller_requires_the_moderation_manage_permission()
    {
        var permission = Controller.GetCustomAttribute<HasPermissionAttribute>();

        Assert.NotNull(permission);
        Assert.Equal(ExpectedPolicy, permission.Policy);
    }

    [Fact]
    public void No_action_widens_the_actor_types_or_the_permission()
    {
        var actions = Actions().ToList();
        Assert.Equal(8, actions.Count);

        foreach (var action in actions)
        {
            var widened = action.GetCustomAttribute<AllowActorTypesAttribute>();
            Assert.True(
                widened is null || widened.ActorTypes.SequenceEqual([ActorType.PlatformAdmin]),
                $"{action.Name} widens the actor types."
            );
            var permission = action.GetCustomAttribute<HasPermissionAttribute>();
            Assert.True(
                permission is null || permission.Policy == ExpectedPolicy,
                $"{action.Name} overrides the permission."
            );
        }
    }

    [Fact]
    public void Routes_live_under_storage_admin_content_moderation()
    {
        var route = Controller.GetCustomAttribute<RouteAttribute>();

        Assert.NotNull(route);
        Assert.Equal("storage/admin/content-moderation", route.Template);
    }
}
