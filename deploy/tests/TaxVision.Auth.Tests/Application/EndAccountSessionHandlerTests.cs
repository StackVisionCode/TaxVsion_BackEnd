using TaxVision.Auth.Application.AccountSessions.Commands;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.RefreshTokens;

namespace TaxVision.Auth.Tests.Application;

public sealed class EndAccountSessionHandlerTests
{
    [Fact]
    public async Task Signing_out_of_the_account_keeps_the_workspace_session_it_came_from()
    {
        var world = new AccountSessionFixture();
        var (sessionId, _) = await world.StartWorkspaceSessionAsync();
        var session = (await world.Sessions.GetSessionByIdAsync(sessionId))!;
        var account = await world.Issuer.JoinSessionAsync(
            session,
            world.Admin,
            "UTC",
            [],
            ["handoff"],
            SessionSurface.Account
        );

        await HandleAsync(world, account.RefreshToken);

        Assert.True(session.IsActive);
        Assert.Empty(world.Denylist.Denied);
        Assert.False(await world.Sessions.HasActiveChainAsync(sessionId, SessionSurface.Account));
        Assert.True(await world.Sessions.HasActiveChainAsync(sessionId, SessionSurface.Workspace));
        Assert.Contains(world.Audit.Logs, log => log.Action == AuthAuditAction.AccountSessionEnded);
    }

    [Fact]
    public async Task Signing_out_of_an_account_only_session_closes_it_everywhere()
    {
        var world = new AccountSessionFixture();
        var account = await world.Issuer.StartSessionAsync(
            world.Admin,
            "UTC",
            [],
            ["pwd"],
            null,
            SessionSurface.Account
        );

        await HandleAsync(world, account.RefreshToken);

        var session = await world.Sessions.GetSessionByIdAsync(account.SessionId);
        Assert.False(session!.IsActive);
        Assert.Contains(account.SessionId, world.Denylist.Denied);
    }

    [Fact]
    public async Task A_workspace_refresh_token_cannot_close_anything_from_the_account()
    {
        var world = new AccountSessionFixture();
        var (sessionId, workspaceRefresh) = await world.StartWorkspaceSessionAsync();

        await HandleAsync(world, workspaceRefresh);

        Assert.True((await world.Sessions.GetSessionByIdAsync(sessionId))!.IsActive);
        Assert.True(await world.Sessions.HasActiveChainAsync(sessionId, SessionSurface.Workspace));
    }

    private static Task HandleAsync(AccountSessionFixture world, string refreshToken) =>
        EndAccountSessionHandler.Handle(
            new EndAccountSessionCommand(refreshToken),
            world.Sessions,
            world.TokenService,
            world.Denylist,
            world.Audit,
            world.Request,
            world.Correlation,
            world.UnitOfWork,
            CancellationToken.None
        );
}
