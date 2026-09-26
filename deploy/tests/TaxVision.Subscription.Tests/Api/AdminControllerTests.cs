using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Mvc.Routing;
using TaxVision.Subscription.Api.Controllers;
using TaxVision.Subscription.Api.Controllers.Admin;

namespace TaxVision.Subscription.Tests.Api;

public sealed class AdminControllerTests
{
    [Theory]
    [InlineData(nameof(AdminController.RenewSeat), "tenants/{tenantId:guid}/seats/{seatId:guid}/renew")]
    [InlineData(nameof(AdminController.RenewAddOn), "tenants/{tenantId:guid}/addons/{tenantAddOnId:guid}/renew")]
    public void Free_renewals_are_platform_admin_actions_guarded_by_the_renew_permission(string action, string route)
    {
        var method = typeof(AdminController).GetMethod(action)!;

        Assert.Equal(
            new[] { ActorType.PlatformAdmin },
            typeof(AdminController).GetCustomAttribute<AllowActorTypesAttribute>()!.ActorTypes
        );
        Assert.Contains(
            method.GetCustomAttributes<HasPermissionAttribute>(),
            attribute => attribute.Policy == HasPermissionAttribute.PolicyPrefix + SubscriptionPermissions.Renew
        );
        Assert.Equal(route, method.GetCustomAttribute<HttpMethodAttribute>()!.Template);
    }

    [Theory]
    [InlineData(typeof(SeatsController))]
    [InlineData(typeof(AddOnsController))]
    public void Tenant_controllers_expose_no_free_renewal(Type controller)
    {
        var renewRoutes = controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.GetCustomAttribute<HttpMethodAttribute>()?.Template)
            .Where(template => template is not null && template.EndsWith("/renew", StringComparison.Ordinal));

        Assert.Empty(renewRoutes);
    }
}
