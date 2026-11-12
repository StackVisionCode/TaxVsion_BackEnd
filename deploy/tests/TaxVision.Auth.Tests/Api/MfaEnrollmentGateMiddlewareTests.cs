using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using TaxVision.Auth.Api.Middleware;

namespace TaxVision.Auth.Tests.Api;

/// <summary>
/// El token solo-MFA (la oficina exige MFA y la persona no lo configuró) solo sirve para configurarlo. Todo lo demás
/// de Auth responde 403: denegar por defecto, con una lista blanca exacta.
/// </summary>
public sealed class MfaEnrollmentGateMiddlewareTests
{
    private static async Task<(int Status, bool Passed)> SendAsync(
        string method,
        string path,
        bool enrollmentOnly,
        bool anonymousEndpoint = false
    )
    {
        var passed = false;
        var middleware = new MfaEnrollmentGateMiddleware(_ =>
        {
            passed = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        var claims = new List<Claim> { new("sub", Guid.NewGuid().ToString()) };
        if (enrollmentOnly)
            claims.Add(new Claim(MfaEnrollmentToken.ClaimType, MfaEnrollmentToken.Pending));
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        if (anonymousEndpoint)
            context.SetEndpoint(
                new Endpoint(null, new EndpointMetadataCollection(new AllowAnonymousAttribute()), "anon")
            );

        await middleware.InvokeAsync(context);
        return (context.Response.StatusCode, passed);
    }

    [Theory]
    [InlineData("POST", "/auth/mfa/totp/setup")]
    [InlineData("POST", "/auth/mfa/totp/confirm")]
    [InlineData("GET", "/auth/mfa/status")]
    [InlineData("POST", "/auth/logout")]
    public async Task The_enrollment_token_reaches_only_the_mfa_setup_endpoints(string method, string path)
    {
        var (_, passed) = await SendAsync(method, path, enrollmentOnly: true);

        Assert.True(passed);
    }

    [Theory]
    [InlineData("GET", "/auth/me")]
    [InlineData("GET", "/auth/me/access")]
    [InlineData("POST", "/auth/mfa/disable")]
    [InlineData("POST", "/auth/mfa/recovery-codes/regenerate")]
    [InlineData("PUT", "/auth/mfa/policy")]
    [InlineData("POST", "/auth/users/invite")]
    [InlineData("GET", "/auth/mfa/totp/setup")]
    public async Task Anything_else_is_forbidden_with_the_enrollment_token(string method, string path)
    {
        var (status, passed) = await SendAsync(method, path, enrollmentOnly: true);

        Assert.False(passed);
        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    [Fact]
    public async Task Anonymous_endpoints_like_refresh_keep_working()
    {
        var (_, passed) = await SendAsync("POST", "/auth/refresh", enrollmentOnly: true, anonymousEndpoint: true);

        Assert.True(passed);
    }

    [Fact]
    public async Task A_normal_session_is_not_affected()
    {
        var (_, passed) = await SendAsync("GET", "/auth/me", enrollmentOnly: false);

        Assert.True(passed);
    }

    // Si alguien agrega una ruta a la lista blanca, esta prueba obliga a revisarlo.
    [Fact]
    public void The_whitelist_is_exactly_the_mfa_setup_flow()
    {
        Assert.Equal(
            [
                ("POST", "/auth/mfa/totp/setup"),
                ("POST", "/auth/mfa/totp/confirm"),
                ("GET", "/auth/mfa/status"),
                ("POST", "/auth/logout"),
            ],
            MfaEnrollmentToken.AllowedRoutes
        );
    }
}
