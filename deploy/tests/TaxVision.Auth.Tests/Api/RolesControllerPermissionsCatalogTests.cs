using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Api.Controllers;
using TaxVision.Auth.Application.Roles.Queries;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Api;

/// <summary>
/// El catálogo de permisos solo incluye los PlatformOnly para un PlatformAdmin. La decisión sale del
/// claim actor_type del JWT firmado, no de nada que el navegador pueda cambiar.
/// </summary>
public sealed class RolesControllerPermissionsCatalogTests
{
    private static RolesController ControllerFor(FakeMessageBus bus, string actorType)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimNames.TenantId, Guid.NewGuid().ToString()), new Claim(ClaimNames.ActorType, actorType)],
            authenticationType: "test"
        );
        return new RolesController(bus)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
            },
        };
    }

    private static FakeMessageBus CatalogBus() =>
        new() { InvokeHandler = _ => Result.Success<IReadOnlyList<PermissionResponse>>([]) };

    [Theory]
    [InlineData("TenantAdmin")]
    [InlineData("TenantEmployee")]
    public async Task An_office_user_asks_for_the_catalog_without_platform_only_permissions(string actorType)
    {
        var bus = CatalogBus();

        var response = await ControllerFor(bus, actorType).GetPermissionsCatalog(CancellationToken.None);

        Assert.IsType<OkObjectResult>(response);
        var query = Assert.IsType<GetPermissionsCatalogQuery>(Assert.Single(bus.Invoked));
        Assert.False(query.IncludePlatformOnly);
    }

    [Fact]
    public async Task A_platform_admin_asks_for_the_full_catalog()
    {
        var bus = CatalogBus();

        await ControllerFor(bus, "PlatformAdmin").GetPermissionsCatalog(CancellationToken.None);

        var query = Assert.IsType<GetPermissionsCatalogQuery>(Assert.Single(bus.Invoked));
        Assert.True(query.IncludePlatformOnly);
    }
}
