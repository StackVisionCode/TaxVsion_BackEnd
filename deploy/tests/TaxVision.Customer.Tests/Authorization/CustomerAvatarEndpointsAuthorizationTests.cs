using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using TaxVision.Customer.Api.Controllers;

namespace TaxVision.Customer.Tests.Authorization;

/// <summary>
/// La foto del cliente tiene dos puertas y no se cruzan: el staff entra por <c>customers/{id}/avatar</c>
/// (con <c>customers.manage</c>) y el cliente del portal solo por <c>customers/me/avatar</c>.
/// </summary>
public sealed class CustomerAvatarEndpointsAuthorizationTests
{
    private static readonly ActorType[] StaffActors =
    [
        ActorType.TenantEmployee,
        ActorType.TenantAdmin,
        ActorType.PlatformAdmin,
    ];

    [Theory]
    [InlineData(nameof(CustomerController.UploadAvatar), "PUT")]
    [InlineData(nameof(CustomerController.RemoveAvatar), "DELETE")]
    public void Staff_endpoints_reject_the_portal_and_require_customers_manage(string actionName, string verb)
    {
        var action = Action(typeof(CustomerController), actionName);

        var actors = EffectiveActorTypes(typeof(CustomerController), action);
        Assert.DoesNotContain(ActorType.CustomerPortal, actors);
        Assert.Equal(StaffActors.OrderBy(a => a), actors.OrderBy(a => a));

        var permission = action.GetCustomAttribute<HasPermissionAttribute>();
        Assert.NotNull(permission);
        Assert.Equal(HasPermissionAttribute.PolicyPrefix + CustomersPermissions.Manage, permission!.Policy);

        AssertRoute(action, verb, "{id:guid}/avatar");
        Assert.Equal("customer.g.write", action.GetCustomAttribute<RateLimitAttribute>()?.PolicyName);
    }

    [Theory]
    [InlineData(nameof(PortalCustomerController.UploadAvatar), "PUT")]
    [InlineData(nameof(PortalCustomerController.RemoveAvatar), "DELETE")]
    public void Portal_endpoints_only_admit_the_customer_portal(string actionName, string verb)
    {
        var action = Action(typeof(PortalCustomerController), actionName);

        var actors = EffectiveActorTypes(typeof(PortalCustomerController), action);
        Assert.Equal([ActorType.CustomerPortal], actors);
        foreach (var staff in StaffActors)
            Assert.DoesNotContain(staff, actors);

        AssertRoute(action, verb, "avatar");
        Assert.Equal("customer.g.write", action.GetCustomAttribute<RateLimitAttribute>()?.PolicyName);
    }

    [Fact]
    public void Upload_endpoints_cap_the_request_size_around_the_image_limit()
    {
        foreach (var controller in new[] { typeof(CustomerController), typeof(PortalCustomerController) })
        {
            var limit = Action(controller, "UploadAvatar").GetCustomAttribute<RequestSizeLimitAttribute>();
            Assert.NotNull(limit);
        }
    }

    [Fact]
    public void Customer_service_routes_avatar_deletions_to_the_cloudstorage_queue()
    {
        var program = File.ReadAllText(ProgramPath());

        Assert.Contains(
            "PublishMessage<FilesDeletionRequestedIntegrationEvent>().ToRabbitQueue(\"cloudstorage-file-deletions\")",
            program,
            StringComparison.Ordinal
        );
    }

    private static MethodInfo Action(Type controller, string name) =>
        controller.GetMethod(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        ?? throw new InvalidOperationException($"{controller.Name}.{name} not found.");

    private static ActorType[] EffectiveActorTypes(Type controller, MethodInfo action) =>
        (
            action.GetCustomAttribute<AllowActorTypesAttribute>()
            ?? controller.GetCustomAttribute<AllowActorTypesAttribute>(inherit: true)
        )?.ActorTypes.ToArray() ?? [];

    private static void AssertRoute(MethodInfo action, string verb, string template)
    {
        var http = Assert.Single(action.GetCustomAttributes().OfType<HttpMethodAttribute>());
        Assert.Contains(verb, http.HttpMethods);
        Assert.Equal(template, http.Template);
    }

    private static string ProgramPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "Services", "Customer", "TaxVision.Customer.Api", "Program.cs");
    }
}
