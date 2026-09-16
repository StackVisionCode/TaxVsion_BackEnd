using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Api.Controllers;
using TaxVision.Auth.Application.Permissions.Commands;
using TaxVision.Auth.Application.Permissions.Queries;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Api;

/// <summary>
/// Controller contract for the deny-layer endpoints on <see cref="UsersController"/>: the command/query
/// is built from the route + body + caller claims, a success maps to the documented status (204 PUT /
/// 200 GET), a handler failure maps through <c>Error.ToHttpStatusCode()</c> to the right status, and a
/// missing tenant/user claim short-circuits to 401 before the bus is touched.
/// </summary>
public sealed class UsersControllerPermissionOverridesTests
{
    private static UsersController ControllerFor(FakeMessageBus bus, Guid tenantId, Guid callerId)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimNames.TenantId, tenantId.ToString()), new Claim("sub", callerId.ToString())],
            authenticationType: "test"
        );
        return new UsersController(bus)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
            },
        };
    }

    private static UsersController AnonymousController(FakeMessageBus bus) =>
        new(bus) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    [Fact]
    public async Task Put_builds_the_command_from_claims_and_body_and_returns_204()
    {
        var tenantId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var denyId = Guid.NewGuid();
        var bus = new FakeMessageBus { InvokeHandler = _ => Result.Success() };

        var response = await ControllerFor(bus, tenantId, callerId)
            .SetPermissionOverrides(
                targetId,
                new UsersController.SetPermissionOverridesRequest([denyId]),
                CancellationToken.None
            );

        Assert.IsType<NoContentResult>(response);
        var command = Assert.IsType<SetUserPermissionOverridesCommand>(Assert.Single(bus.Invoked));
        Assert.Equal(tenantId, command.TenantId);
        Assert.Equal(targetId, command.TargetUserId);
        Assert.Equal(callerId, command.RequestedByUserId);
        Assert.Equal(new[] { denyId }, command.DeniedPermissionIds);
    }

    [Fact]
    public async Task Put_maps_a_self_action_failure_to_400()
    {
        var bus = new FakeMessageBus { InvokeHandler = _ => Result.Failure(new Error("User.SelfAction", "no")) };

        var response = await ControllerFor(bus, Guid.NewGuid(), Guid.NewGuid())
            .SetPermissionOverrides(
                Guid.NewGuid(),
                new UsersController.SetPermissionOverridesRequest([]),
                CancellationToken.None
            );

        var objectResult = Assert.IsType<ObjectResult>(response);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    [Fact]
    public async Task Put_maps_a_not_found_failure_to_404()
    {
        var bus = new FakeMessageBus { InvokeHandler = _ => Result.Failure(new Error("User.NotFound", "no")) };

        var response = await ControllerFor(bus, Guid.NewGuid(), Guid.NewGuid())
            .SetPermissionOverrides(
                Guid.NewGuid(),
                new UsersController.SetPermissionOverridesRequest([]),
                CancellationToken.None
            );

        var objectResult = Assert.IsType<ObjectResult>(response);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    [Fact]
    public async Task Put_without_claims_is_401_and_never_touches_the_bus()
    {
        var bus = new FakeMessageBus();

        var response = await AnonymousController(bus)
            .SetPermissionOverrides(
                Guid.NewGuid(),
                new UsersController.SetPermissionOverridesRequest([]),
                CancellationToken.None
            );

        Assert.IsType<UnauthorizedResult>(response);
        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public async Task Get_builds_the_query_from_claims_and_returns_200_with_the_body()
    {
        var tenantId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var expected = new UserEffectiveAccessResponse(targetId, "TenantEmployee", ["Administrator"], [], 3);
        var bus = new FakeMessageBus { InvokeHandler = _ => Result.Success(expected) };

        var response = await ControllerFor(bus, tenantId, Guid.NewGuid())
            .GetEffectiveAccess(targetId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response);
        Assert.Same(expected, ok.Value);
        var queryMessage = Assert.IsType<GetUserEffectiveAccessQuery>(Assert.Single(bus.Invoked));
        Assert.Equal(tenantId, queryMessage.TenantId);
        Assert.Equal(targetId, queryMessage.TargetUserId);
    }

    [Fact]
    public async Task Get_maps_a_not_found_failure_to_404()
    {
        var bus = new FakeMessageBus
        {
            InvokeHandler = _ => Result.Failure<UserEffectiveAccessResponse>(new Error("User.NotFound", "no")),
        };

        var response = await ControllerFor(bus, Guid.NewGuid(), Guid.NewGuid())
            .GetEffectiveAccess(Guid.NewGuid(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(response);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }
}
