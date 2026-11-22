using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Auth.Application.Sessions;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Sessions;

namespace TaxVision.Auth.Tests.Application;

/// <summary>El barrido cierra las sesiones sin ningún refresh vigente y avisa una sola vez por sesión.</summary>
public sealed class ExpiredSessionSweepTests
{
    private static readonly DateTime Now = DateTime.UtcNow;
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Closes_and_publishes_once()
    {
        var sessions = new InMemorySessionRepository();
        var expired = await AddSessionAsync(sessions, Now.AddDays(-1), SessionSurface.Mobile);
        var alive = await AddSessionAsync(sessions, Now.AddDays(1));
        var bus = new FakeMessageBus();

        await SweepAsync(sessions, bus);
        await SweepAsync(sessions, bus);

        var ended = Assert.Single(bus.Published.OfType<SessionEndedIntegrationEvent>());
        Assert.Equal(expired.Id, ended.SessionId);
        Assert.Null(ended.ExceptSessionId);
        Assert.Equal("expired", ended.Reason);
        Assert.Equal(TenantId, ended.TenantId);
        Assert.Equal(UserId, ended.UserId);
        Assert.Equal("Mobile", ended.Channel);
        Assert.Equal(Now, ended.EndedAtUtc);
        Assert.False(expired.IsActive);
        Assert.Equal("expired", expired.RevokedReason);
        Assert.True(alive.IsActive);
    }

    [Fact]
    public async Task A_session_closed_in_between_is_left_alone()
    {
        var sessions = new InMemorySessionRepository();
        var session = await AddSessionAsync(sessions, Now.AddDays(-1));
        session.Revoke("user_logout");
        var bus = new FakeMessageBus();

        var closed = await ExpiredSessionSweep.CloseAsync(session.Id, sessions, bus, Now, CancellationToken.None);

        Assert.False(closed);
        Assert.Empty(bus.Published);
        Assert.Equal("user_logout", session.RevokedReason);
    }

    private static async Task SweepAsync(InMemorySessionRepository sessions, FakeMessageBus bus)
    {
        foreach (var sessionId in await sessions.FindExpiredSessionIdsAsync(Now, ExpiredSessionSweep.BatchSize))
            await ExpiredSessionSweep.CloseAsync(sessionId, sessions, bus, Now, CancellationToken.None);
    }

    private static async Task<UserSession> AddSessionAsync(
        InMemorySessionRepository sessions,
        DateTime refreshExpiresAtUtc,
        SessionSurface surface = SessionSurface.Workspace
    )
    {
        var session = UserSession.Start(TenantId, UserId, "Chrome", "agent", "203.0.113.10", surface);
        await sessions.AddSessionAsync(session);
        await sessions.AddTokenAsync(
            RefreshToken.Create(TenantId, UserId, session.Id, $"hash-{session.Id}", refreshExpiresAtUtc, surface)
        );
        return session;
    }
}
