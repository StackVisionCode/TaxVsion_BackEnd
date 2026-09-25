using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.AccountSessions.Commands;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.RefreshTokens;

namespace TaxVision.Auth.Tests.Application;

public sealed class ExchangeAccountHandoffHandlerTests
{
    [Fact]
    public async Task Redeeming_joins_the_same_session_with_its_own_account_chain()
    {
        var world = new AccountSessionFixture();
        var (sessionId, _) = await world.StartWorkspaceSessionAsync();
        var ticket = await IssueTicketAsync(world, sessionId);

        var result = await HandleAsync(world, ticket);

        Assert.True(result.IsSuccess);
        Assert.Single(world.Sessions.Sessions);
        var (issuedSid, surface, _, _) = world.Jwt.Issued[^1];
        Assert.Equal(sessionId, issuedSid);
        Assert.Equal(SessionSurface.Account, surface);
        Assert.True(await world.Sessions.HasActiveChainAsync(sessionId, SessionSurface.Account));
        Assert.True(await world.Sessions.HasActiveChainAsync(sessionId, SessionSurface.Workspace));
        Assert.Contains(world.Audit.Logs, log => log.Action == AuthAuditAction.AccountSessionStarted);
    }

    [Fact]
    public async Task The_ticket_works_only_once()
    {
        var world = new AccountSessionFixture();
        var (sessionId, _) = await world.StartWorkspaceSessionAsync();
        var ticket = await IssueTicketAsync(world, sessionId);
        await HandleAsync(world, ticket);

        var replay = await HandleAsync(world, ticket);

        Assert.Equal("Auth.HandoffInvalid", replay.Error.Code);
    }

    [Fact]
    public async Task A_session_closed_while_the_ticket_travelled_is_rejected()
    {
        var world = new AccountSessionFixture();
        var (sessionId, _) = await world.StartWorkspaceSessionAsync();
        var ticket = await IssueTicketAsync(world, sessionId);
        await world.Sessions.RevokeSessionAsync(sessionId, "single_session_superseded");

        var result = await HandleAsync(world, ticket);

        Assert.Equal("Auth.HandoffInvalid", result.Error.Code);
    }

    [Fact]
    public async Task A_new_handoff_replaces_the_previous_account_chain()
    {
        var world = new AccountSessionFixture();
        var (sessionId, _) = await world.StartWorkspaceSessionAsync();
        var first = await HandleAsync(world, await IssueTicketAsync(world, sessionId));

        await HandleAsync(world, await IssueTicketAsync(world, sessionId));

        var firstToken = await world.Sessions.GetTokenByHashAsync(world.TokenService.Hash(first.Value.RefreshToken));
        Assert.False(firstToken!.IsActive);
        Assert.Single(world.Sessions.Tokens, token => token.Surface == SessionSurface.Account && token.IsActive);
    }

    private static async Task<Guid> IssueTicketAsync(AccountSessionFixture world, Guid sessionId) =>
        await world.HandoffTickets.IssueAsync(new AccountHandoffPayload(world.Tenant.Id, world.Admin.Id, sessionId));

    private static Task<Result<AuthTokensResponse>> HandleAsync(AccountSessionFixture world, Guid ticket) =>
        ExchangeAccountHandoffHandler.Handle(
            new ExchangeAccountHandoffCommand(ticket),
            world.HandoffTickets,
            world.Users,
            world.Tenants,
            world.Roles,
            world.Sessions,
            world.Issuer,
            world.Audit,
            world.Request,
            world.Correlation,
            world.UnitOfWork,
            CancellationToken.None
        );
}
