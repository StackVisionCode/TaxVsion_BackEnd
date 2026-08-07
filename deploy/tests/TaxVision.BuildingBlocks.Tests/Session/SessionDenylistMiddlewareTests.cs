using System.Security.Claims;
using BuildingBlocks.Sessions;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Session;

public sealed class SessionDenylistMiddlewareTests
{
    [Fact]
    public async Task Returns_401_when_the_session_is_in_the_denylist()
    {
        var sessionId = Guid.NewGuid();
        var context = BuildContext(sessionId);
        var nextCalled = false;

        await Invoke(
            context,
            denylist: new FakeDenylistReader(deniedSessionId: sessionId),
            next: _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            }
        );

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task Passes_through_when_the_session_is_not_in_the_denylist()
    {
        var context = BuildContext(Guid.NewGuid());
        var nextCalled = false;

        await Invoke(
            context,
            denylist: new FakeDenylistReader(deniedSessionId: null),
            next: _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            }
        );

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task Ignores_service_tokens_that_have_no_sid_claim()
    {
        // M2M (client_credentials) tokens no llevan sid — la denylist es un concepto de sesión de
        // usuario, no de actor de servicio. TryGetSessionId falla y el middleware nunca consulta
        // al reader (probamos que igual pasa incluso con un reader configurado para denegar todo).
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("actor_type", "Service")], authenticationType: "Test")
        );
        var nextCalled = false;

        await Invoke(
            context,
            denylist: new FakeDenylistReader(denyEverything: true),
            next: _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            }
        );

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task H06_con_FailOpen_deja_pasar_si_el_store_no_responde()
    {
        var context = BuildContext(Guid.NewGuid());
        var nextCalled = false;

        await Invoke(
            context,
            denylist: new UnavailableDenylistReader(),
            next: _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            failureMode: SessionDenylistFailureMode.FailOpen
        );

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task H06_con_FailClosed_responde_503_si_el_store_no_responde()
    {
        var context = BuildContext(Guid.NewGuid());
        var nextCalled = false;

        await Invoke(
            context,
            denylist: new UnavailableDenylistReader(),
            next: _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            failureMode: SessionDenylistFailureMode.FailClosed
        );

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
    }

    private static async Task Invoke(
        HttpContext context,
        ISessionDenylistReader denylist,
        RequestDelegate next,
        SessionDenylistFailureMode failureMode = SessionDenylistFailureMode.FailOpen
    )
    {
        var options = Options.Create(new SessionDenylistOptions { Enabled = true, FailureMode = failureMode });
        var middleware = new SessionDenylistMiddleware(next);
        await middleware.InvokeAsync(
            context,
            denylist,
            options,
            new AuthorizationMetrics(),
            NullLogger<SessionDenylistMiddleware>.Instance
        );
    }

    private static DefaultHttpContext BuildContext(Guid sessionId)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim("sid", sessionId.ToString()), new Claim("actor_type", "TenantEmployee")],
                authenticationType: "Test"
            )
        );
        return context;
    }

    private sealed class UnavailableDenylistReader : ISessionDenylistReader
    {
        public Task<bool> IsSessionDeniedAsync(Guid sessionId, CancellationToken ct = default) =>
            throw new SessionDenylistUnavailableException(sessionId, new InvalidOperationException("Redis down."));
    }

    private sealed class FakeDenylistReader(Guid? deniedSessionId = null, bool denyEverything = false)
        : ISessionDenylistReader
    {
        public Task<bool> IsSessionDeniedAsync(Guid sessionId, CancellationToken ct = default) =>
            Task.FromResult(denyEverything || sessionId == deniedSessionId);
    }
}
