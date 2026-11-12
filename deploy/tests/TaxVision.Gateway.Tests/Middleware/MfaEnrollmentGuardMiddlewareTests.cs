using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using Microsoft.AspNetCore.Http;
using TaxVision.Gateway.Middleware;
using Xunit;

namespace TaxVision.Gateway.Tests.Middleware;

/// <summary>Segunda capa: el token solo-MFA no sale de <c>/auth/</c> por el gateway.</summary>
public sealed class MfaEnrollmentGuardMiddlewareTests
{
    private static async Task<(int Status, bool Passed)> SendAsync(string path, bool enrollmentOnly)
    {
        var passed = false;
        var middleware = new MfaEnrollmentGuardMiddleware(_ =>
        {
            passed = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        var claims = new List<Claim>();
        if (enrollmentOnly)
            claims.Add(new Claim(MfaEnrollmentToken.ClaimType, MfaEnrollmentToken.Pending));
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));

        await middleware.InvokeAsync(context);
        return (context.Response.StatusCode, passed);
    }

    [Theory]
    [InlineData("/customers")]
    [InlineData("/subscriptions/me")]
    [InlineData("/storage/files")]
    [InlineData("/communication/conversations")]
    [InlineData("/authx/something")]
    public async Task The_enrollment_token_never_leaves_auth(string path)
    {
        var (status, passed) = await SendAsync(path, enrollmentOnly: true);

        Assert.False(passed);
        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    [Fact]
    public async Task The_enrollment_token_can_reach_auth_which_decides_the_endpoint()
    {
        var (_, passed) = await SendAsync("/auth/mfa/totp/setup", enrollmentOnly: true);

        Assert.True(passed);
    }

    [Fact]
    public async Task A_normal_session_is_not_affected()
    {
        var (_, passed) = await SendAsync("/customers", enrollmentOnly: false);

        Assert.True(passed);
    }
}
