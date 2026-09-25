using System.Reflection;
using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Security;

public sealed class SurfaceAuthorizationFilterTests
{
    [Fact]
    public void Workspace_tokens_are_not_affected()
    {
        var context = BuildContext(nameof(WorkspaceController.Roles), typeof(WorkspaceController), surface: null);

        new SurfaceAuthorizationFilter().OnAuthorization(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public void An_account_token_is_rejected_where_the_action_does_not_allow_the_surface()
    {
        var context = BuildContext(
            nameof(WorkspaceController.Roles),
            typeof(WorkspaceController),
            AccessSurface.Account
        );

        new SurfaceAuthorizationFilter().OnAuthorization(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Equal("Auth.SurfaceNotAllowed", Assert.IsType<Error>(result.Value).Code);
    }

    [Fact]
    public void An_account_token_passes_where_the_action_allows_the_surface()
    {
        var context = BuildContext(nameof(WorkspaceController.Me), typeof(WorkspaceController), AccessSurface.Account);

        new SurfaceAuthorizationFilter().OnAuthorization(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public void The_controller_can_allow_the_surface_for_all_its_actions()
    {
        var context = BuildContext(
            nameof(AccountController.Overview),
            typeof(AccountController),
            AccessSurface.Account
        );

        new SurfaceAuthorizationFilter().OnAuthorization(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public void Anonymous_actions_are_skipped()
    {
        var context = BuildContext(
            nameof(WorkspaceController.Public),
            typeof(WorkspaceController),
            AccessSurface.Account
        );

        new SurfaceAuthorizationFilter().OnAuthorization(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public void An_unknown_surface_is_rejected_even_where_account_is_allowed()
    {
        var context = BuildContext(nameof(WorkspaceController.Me), typeof(WorkspaceController), "kiosk");

        new SurfaceAuthorizationFilter().OnAuthorization(context);

        Assert.IsType<ObjectResult>(context.Result);
    }

    private static AuthorizationFilterContext BuildContext(string methodName, Type controllerType, string? surface)
    {
        var descriptor = new ControllerActionDescriptor
        {
            MethodInfo = controllerType.GetMethod(methodName)!,
            ControllerTypeInfo = controllerType.GetTypeInfo(),
        };

        List<Claim> claims = [new(ClaimNames.ActorType, ActorType.TenantAdmin.ToString())];
        if (surface is not null)
            claims.Add(new Claim(ClaimNames.Surface, surface));

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test")),
        };
        return new AuthorizationFilterContext(new ActionContext(httpContext, new RouteData(), descriptor), []);
    }

    private sealed class WorkspaceController
    {
        public void Roles() { }

        [AllowSurface(AccessSurface.Account)]
        public void Me() { }

        [AllowAnonymous]
        public void Public() { }
    }

    [AllowSurface(AccessSurface.Account)]
    private sealed class AccountController
    {
        public void Overview() { }
    }
}
