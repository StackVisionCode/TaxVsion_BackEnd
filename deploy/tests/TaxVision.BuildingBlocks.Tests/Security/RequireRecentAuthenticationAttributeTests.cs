using System.Globalization;
using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Security;

public sealed class RequireRecentAuthenticationAttributeTests
{
    [Fact]
    public void A_recent_reauthentication_passes()
    {
        var context = BuildContext(reauthenticatedAgo: TimeSpan.FromMinutes(2));

        new RequireRecentAuthenticationAttribute().OnAuthorization(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public void Without_reauthentication_it_asks_for_it_with_rfc_9470_challenge()
    {
        var context = BuildContext(reauthenticatedAgo: null);

        new RequireRecentAuthenticationAttribute().OnAuthorization(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
        Assert.Equal("Auth.ReauthenticationRequired", Assert.IsType<Error>(result.Value).Code);
        var challenge = context.HttpContext.Response.Headers.WWWAuthenticate.ToString();
        Assert.Contains("error=\"insufficient_user_authentication\"", challenge);
        Assert.Contains("max_age=600", challenge);
    }

    [Fact]
    public void An_expired_reauthentication_asks_again()
    {
        var context = BuildContext(reauthenticatedAgo: TimeSpan.FromMinutes(11));

        new RequireRecentAuthenticationAttribute().OnAuthorization(context);

        Assert.IsType<ObjectResult>(context.Result);
    }

    [Fact]
    public void The_window_is_configurable()
    {
        var context = BuildContext(reauthenticatedAgo: TimeSpan.FromMinutes(4));

        new RequireRecentAuthenticationAttribute { Minutes = 3 }.OnAuthorization(context);

        Assert.IsType<ObjectResult>(context.Result);
    }

    [Fact]
    public void Anonymous_requests_are_left_to_the_authentication_pipeline()
    {
        var httpContext = new DefaultHttpContext();
        var context = new AuthorizationFilterContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            []
        );

        new RequireRecentAuthenticationAttribute().OnAuthorization(context);

        Assert.Null(context.Result);
    }

    private static AuthorizationFilterContext BuildContext(TimeSpan? reauthenticatedAgo)
    {
        List<Claim> claims = [new("sub", Guid.NewGuid().ToString())];
        if (reauthenticatedAgo is { } ago)
        {
            var epoch = DateTimeOffset.UtcNow.Subtract(ago).ToUnixTimeSeconds();
            claims.Add(new Claim(ClaimNames.ReauthenticatedAt, epoch.ToString(CultureInfo.InvariantCulture)));
        }

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test")),
        };
        return new AuthorizationFilterContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            []
        );
    }
}
