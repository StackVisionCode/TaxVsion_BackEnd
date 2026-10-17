using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using TaxVision.Subscription.Api.Controllers.Admin;
using Xunit;

namespace TaxVision.Subscription.Tests.Api;

public sealed class ModulesAdminControllerTests
{
    [Fact]
    public void Controller_is_restricted_to_platform_admin_with_cross_tenant_permission()
    {
        var controller = typeof(ModulesAdminController);

        Assert.Equal("admin/subscription/modules", controller.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Contains(ActorType.PlatformAdmin, controller.GetCustomAttribute<AllowActorTypesAttribute>()!.ActorTypes);
        Assert.Equal(
            HasPermissionAttribute.PolicyPrefix + SubscriptionPermissions.AdminCrossTenant,
            controller.GetCustomAttribute<HasPermissionAttribute>()!.Policy
        );
    }

    [Theory]
    [InlineData(nameof(ModulesAdminController.List), null, "GET")]
    [InlineData(nameof(ModulesAdminController.Update), "{code}", "PUT")]
    [InlineData(nameof(ModulesAdminController.Create), null, "POST")]
    public void Endpoint_contract_is_stable(string methodName, string? template, string verb)
    {
        var action = typeof(ModulesAdminController).GetMethod(methodName)!;
        var route = action.GetCustomAttributes<HttpMethodAttribute>().Single();

        Assert.Equal(template, route.Template);
        Assert.Contains(verb, route.HttpMethods);
    }
}
