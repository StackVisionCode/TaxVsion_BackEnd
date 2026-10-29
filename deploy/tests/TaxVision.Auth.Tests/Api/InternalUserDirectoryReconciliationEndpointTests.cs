using System.Reflection;
using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Common;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Api.Controllers;
using TaxVision.Auth.Application.Users;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Api;

/// <summary>
/// `GET internal/users/directory/reconciliation` — M2M de plataforma: solo un service token del
/// PlatformTenant lee el directorio de todos los tenants; nunca pasa por el rate limit ni por el Gateway.
/// </summary>
public sealed class InternalUserDirectoryReconciliationEndpointTests
{
    private static MethodInfo Action() =>
        typeof(InternalUserReconciliationController).GetMethod(nameof(InternalUserReconciliationController.Directory))!;

    [Fact]
    public void Is_an_internal_service_only_route_exempt_from_rate_limit()
    {
        var controller = typeof(InternalUserReconciliationController);
        Assert.Equal("internal/users", controller.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("ServiceOnly", controller.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal(
            new[] { ActorType.Service },
            controller.GetCustomAttribute<AllowActorTypesAttribute>()!.ActorTypes
        );

        var action = Action();
        Assert.Equal("directory/reconciliation", action.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.NotNull(action.GetCustomAttribute<RateLimitExemptAttribute>());
    }

    [Fact]
    public async Task A_service_token_of_another_tenant_is_forbidden()
    {
        var bus = new FakeMessageBus();
        var controller = Controller(bus, Guid.NewGuid());

        var result = await controller.Directory(1, 200, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public async Task The_platform_service_gets_the_requested_page()
    {
        var row = new UserDirectoryReconciliationResponse(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "TenantEmployee",
            true,
            "Ana Lopez",
            "ana@x.test",
            Guid.NewGuid()
        );
        var bus = new FakeMessageBus
        {
            InvokeHandler = message =>
            {
                var query = Assert.IsType<ReconciliationUserDirectoryQuery>(message);
                return new PagedResult<UserDirectoryReconciliationResponse>([row], query.Page, query.Size, 1);
            },
        };
        var controller = Controller(bus, PlatformTenant.Id);

        var result = await controller.Directory(2, 50, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var page = Assert.IsType<PagedResult<UserDirectoryReconciliationResponse>>(ok.Value);
        Assert.Equal((2, 50), (page.Page, page.Size));
        Assert.Equal(row, Assert.Single(page.Items));
        Assert.Equal(new ReconciliationUserDirectoryQuery(2, 50), Assert.Single(bus.Invoked));
    }

    private static InternalUserReconciliationController Controller(FakeMessageBus bus, Guid tenantId) =>
        new(bus)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity([new Claim(ClaimNames.TenantId, tenantId.ToString())], "test")
                    ),
                },
            },
        };
}
