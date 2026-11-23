using System.Reflection;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.Auth.Api.Controllers;

namespace TaxVision.Auth.Tests.Api;

/// <summary>
/// Salir por inactividad pasa cuando el access token ya venció (dura 15 min y la inactividad 30): el front revoca la
/// sesión con el refresh token, que es el secreto, igual que el refresh. Si pidiera JWT, la sesión quedaba viva.
/// </summary>
public sealed class RevokeEndpointTests
{
    private static MethodInfo Action(string name) => typeof(AuthController).GetMethod(name)!;

    [Fact]
    public void Revoke_works_with_the_refresh_token_alone_like_refresh()
    {
        var revoke = Action(nameof(AuthController.Revoke));

        Assert.NotNull(revoke.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(revoke.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("auth-refresh", revoke.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
    }

    // El Account del landing también muestra el aviso de inactividad: tiene que poder leer la política.
    [Fact]
    public void The_session_policy_is_readable_from_the_account_surface()
    {
        var get = typeof(SessionPolicyController).GetMethod(nameof(SessionPolicyController.Get))!;

        var surfaces = get.GetCustomAttribute<AllowSurfaceAttribute>()?.Surfaces ?? [];
        Assert.Contains("account", surfaces);
    }
}
