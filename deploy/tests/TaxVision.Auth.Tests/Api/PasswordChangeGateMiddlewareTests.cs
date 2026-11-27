using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using TaxVision.Auth.Api.Middleware;

namespace TaxVision.Auth.Tests.Api;

/// <summary>
/// El token de cambio obligatorio (03 §9.1) solo sirve para cambiar la contraseña y cerrar sesión. Todo lo demás de
/// Auth responde 403 <c>Auth.PasswordChangeRequired</c>: denegar por defecto, con una lista blanca exacta.
/// </summary>
public sealed class PasswordChangeGateMiddlewareTests
{
    private static async Task<(int Status, bool Passed, string Body)> SendAsync(
        string method,
        string path,
        bool passwordChangeOnly,
        bool anonymousEndpoint = false
    )
    {
        var passed = false;
        var middleware = new PasswordChangeGateMiddleware(_ =>
        {
            passed = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        var claims = new List<Claim> { new("sub", Guid.NewGuid().ToString()) };
        if (passwordChangeOnly)
            claims.Add(new Claim(PasswordChangeToken.ClaimType, PasswordChangeToken.Required));
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        if (anonymousEndpoint)
            context.SetEndpoint(
                new Endpoint(null, new EndpointMetadataCollection(new AllowAnonymousAttribute()), "anon")
            );

        await middleware.InvokeAsync(context);
        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, passed, await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    [Theory]
    [InlineData("POST", "/auth/password/change")]
    [InlineData("POST", "/auth/logout")]
    public async Task The_password_change_token_reaches_only_the_change_and_logout(string method, string path)
    {
        var (_, passed, _) = await SendAsync(method, path, passwordChangeOnly: true);

        Assert.True(passed);
    }

    [Theory]
    [InlineData("GET", "/auth/me")]
    [InlineData("POST", "/auth/reauthenticate")]
    [InlineData("POST", "/auth/mfa/totp/setup")]
    [InlineData("POST", "/auth/mfa/disable")]
    [InlineData("GET", "/auth/password/change")]
    [InlineData("POST", "/auth/users/invite")]
    public async Task Anything_else_is_forbidden_with_the_password_change_token(string method, string path)
    {
        var (status, passed, body) = await SendAsync(method, path, passwordChangeOnly: true);

        Assert.False(passed);
        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.Contains("Auth.PasswordChangeRequired", body);
    }

    [Fact]
    public async Task Anonymous_endpoints_like_refresh_keep_working()
    {
        var (_, passed, _) = await SendAsync(
            "POST",
            "/auth/refresh",
            passwordChangeOnly: true,
            anonymousEndpoint: true
        );

        Assert.True(passed);
    }

    [Fact]
    public async Task A_normal_session_is_not_affected()
    {
        var (_, passed, _) = await SendAsync("GET", "/auth/me", passwordChangeOnly: false);

        Assert.True(passed);
    }

    // Si alguien agrega una ruta a la lista blanca, esta prueba obliga a revisarlo.
    [Fact]
    public void The_whitelist_is_exactly_change_and_logout()
    {
        Assert.Equal([("POST", "/auth/password/change"), ("POST", "/auth/logout")], PasswordChangeToken.AllowedRoutes);
    }
}
