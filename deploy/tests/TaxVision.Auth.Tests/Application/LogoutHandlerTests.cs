using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Auth.Application.Sessions.Commands;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Cerrar sesión avisa al bus qué sesión terminó: Notification revoca el push de esa sesión.</summary>
public sealed class LogoutHandlerTests
{
    [Fact]
    public async Task Logout_publishes_session_ended()
    {
        var world = new AccountSessionFixture();
        var (sessionId, _) = await world.StartWorkspaceSessionAsync();
        var bus = new FakeMessageBus();

        await LogoutAsync(world, sessionId, bus);

        var ended = Assert.Single(bus.Published.OfType<SessionEndedIntegrationEvent>());
        Assert.Equal(sessionId, ended.SessionId);
        Assert.Null(ended.ExceptSessionId);
        Assert.Equal("user_logout", ended.Reason);
        Assert.Equal(world.Admin.TenantId, ended.TenantId);
        Assert.Equal(world.Admin.Id, ended.UserId);
        Assert.Equal("Web", ended.Channel);
        Assert.Equal(world.Correlation.CorrelationId, ended.CorrelationId);
    }

    [Fact]
    public async Task Logout_of_an_unknown_session_publishes_nothing()
    {
        var world = new AccountSessionFixture();
        var bus = new FakeMessageBus();

        await LogoutAsync(world, Guid.NewGuid(), bus);

        Assert.Empty(bus.Published);
    }

    private static Task LogoutAsync(AccountSessionFixture world, Guid sessionId, FakeMessageBus bus) =>
        LogoutHandler.Handle(
            new LogoutCommand(world.Admin.Id, sessionId),
            world.Sessions,
            world.Denylist,
            world.Audit,
            world.Request,
            world.Correlation,
            world.UnitOfWork,
            bus,
            CancellationToken.None
        );
}
