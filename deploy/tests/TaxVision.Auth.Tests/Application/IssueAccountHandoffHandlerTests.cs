using TaxVision.Auth.Application.AccountSessions.Commands;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

public sealed class IssueAccountHandoffHandlerTests
{
    [Fact]
    public async Task An_admin_with_an_open_session_gets_a_single_use_ticket_bound_to_that_session()
    {
        var world = new AccountSessionFixture();
        var (sessionId, _) = await world.StartWorkspaceSessionAsync();

        var result = await HandleAsync(world, sessionId);

        Assert.True(result.IsSuccess);
        Assert.Equal(60, result.Value.ExpiresInSeconds);
        var payload = await world.HandoffTickets.ConsumeAsync(result.Value.Ticket);
        Assert.Equal(sessionId, payload?.SessionId);
        Assert.Equal(world.Admin.Id, payload?.UserId);
        Assert.Contains(world.Audit.Logs, log => log.Action == AuthAuditAction.AccountHandoffIssued);
    }

    [Fact]
    public async Task Employees_cannot_open_the_account()
    {
        var world = new AccountSessionFixture(UserActorType.TenantEmployee);
        var (sessionId, _) = await world.StartWorkspaceSessionAsync();

        var result = await HandleAsync(world, sessionId);

        Assert.Equal("Auth.AccountAdminOnly", result.Error.Code);
    }

    [Fact]
    public async Task A_closed_session_cannot_be_handed_off()
    {
        var world = new AccountSessionFixture();
        var (sessionId, _) = await world.StartWorkspaceSessionAsync();
        await world.Sessions.RevokeSessionAsync(sessionId, "user_logout");

        var result = await HandleAsync(world, sessionId);

        Assert.Equal("Auth.SessionRevoked", result.Error.Code);
    }

    private static Task<BuildingBlocks.Results.Result<AccountHandoffView>> HandleAsync(
        AccountSessionFixture world,
        Guid sessionId
    ) =>
        IssueAccountHandoffHandler.Handle(
            new IssueAccountHandoffCommand(world.Admin.Id, sessionId),
            world.Users,
            world.Sessions,
            world.HandoffTickets,
            world.Audit,
            world.Request,
            world.Correlation,
            world.UnitOfWork,
            CancellationToken.None
        );
}
