using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Sessions;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// A5 (G10 y R12 del plan) — cortar el acceso son TRES cosas, no una. Revocar en la base solo corta el
/// próximo refresh: el access token ya emitido sigue sirviendo hasta 15 minutos. La suspensión del
/// tenant y el bloqueo por facturación hacían exactamente eso, así que un tenant suspendido seguía
/// operando un cuarto de hora.
/// </summary>
public sealed class SessionAccessCutoffTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static UserSession Session(Guid userId) =>
        UserSession.Start(TenantId, userId, "Chrome en Windows", "agent", "203.0.113.10");

    [Fact]
    public async Task Cutting_a_tenant_denylists_every_session_announces_it_and_then_revokes()
    {
        var first = Session(Guid.NewGuid());
        var second = Session(Guid.NewGuid());
        var sessions = new RecordingSessions([first, second]);
        var denylist = new RecordingDenylist();
        var revocations = new RecordingRevocations();

        var revoked = await SessionAccessCutoff.ForTenantAsync(
            TenantId,
            "tenant_suspended",
            sessions,
            denylist,
            revocations,
            CancellationToken.None
        );

        Assert.Equal(2, revoked);
        Assert.Equal([first.Id, second.Id], denylist.Denied);
        Assert.Equal([first.Id, second.Id], revocations.Published.Select(published => published.SessionId));
        Assert.All(revocations.Published, published => Assert.Equal("tenant_suspended", published.Reason));
        Assert.Equal("tenant_suspended", sessions.TenantRevokeReason);
    }

    /// <summary>
    /// El orden no es un detalle: primero se cierra la puerta (denylist), después se avisa. Al revés, un
    /// cliente avisado podría alcanzar a usar el token viejo antes de que la entrada exista.
    /// </summary>
    [Fact]
    public async Task The_denylist_entry_exists_before_the_announcement_goes_out()
    {
        var session = Session(Guid.NewGuid());
        var order = new List<string>();
        var denylist = new OrderTrackingDenylist(order);
        var revocations = new OrderTrackingRevocations(order);

        await SessionAccessCutoff.ForTenantAsync(
            TenantId,
            "subscription_lapsed",
            new RecordingSessions([session]),
            denylist,
            revocations,
            CancellationToken.None
        );

        Assert.Equal(["deny", "announce"], order);
    }

    [Fact]
    public async Task The_denylist_ttl_outlives_an_access_token()
    {
        var denylist = new RecordingDenylist();

        await SessionAccessCutoff.ForTenantAsync(
            TenantId,
            "tenant_suspended",
            new RecordingSessions([Session(Guid.NewGuid())]),
            denylist,
            new RecordingRevocations(),
            CancellationToken.None
        );

        // El access token vive 15 minutos; el TTL tiene que cubrirlos con margen o la entrada se vence
        // antes que el token que viene a invalidar.
        Assert.True(denylist.Ttl >= TimeSpan.FromMinutes(15));
        Assert.Equal(SessionAccessCutoff.DenylistTtl, denylist.Ttl);
    }

    [Fact]
    public async Task A_tenant_with_no_live_sessions_still_revokes_without_announcing()
    {
        var revocations = new RecordingRevocations();

        await SessionAccessCutoff.ForTenantAsync(
            TenantId,
            "tenant_suspended",
            new RecordingSessions([]),
            new RecordingDenylist(),
            revocations,
            CancellationToken.None
        );

        Assert.Empty(revocations.Published);
    }

    [Fact]
    public async Task Cutting_a_user_uses_the_same_three_steps()
    {
        var user = User.Register(
            TenantId,
            "Ana",
            "Ruiz",
            "ana@example.com",
            "hash",
            UserActorType.TenantEmployee
        ).Value;
        var session = Session(user.Id);
        var denylist = new RecordingDenylist();
        var revocations = new RecordingRevocations();

        await SessionAccessCutoff.ForUserAsync(
            user,
            "admin_revoke",
            new RecordingSessions([session]),
            denylist,
            revocations,
            CancellationToken.None
        );

        Assert.Equal([session.Id], denylist.Denied);
        Assert.Single(revocations.Published);
        Assert.Equal("admin_revoke", revocations.Published[0].Reason);
    }

    // ---------------------------------------------------------------------

    private sealed class RecordingSessions(IReadOnlyList<UserSession> active) : ISessionRepository
    {
        public string? TenantRevokeReason { get; private set; }

        public Task<IReadOnlyList<UserSession>> GetActiveSessionsByTenantAsync(
            Guid tenantId,
            CancellationToken ct = default
        ) => Task.FromResult(active);

        public Task<IReadOnlyList<UserSession>> GetActiveSessionsByUserAsync(
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult(active);

        public Task<int> RevokeAllForTenantAsync(Guid tenantId, string reason, CancellationToken ct = default)
        {
            TenantRevokeReason = reason;
            return Task.FromResult(active.Count);
        }

        public Task<int> RevokeAllForUserAsync(
            Guid userId,
            string reason,
            Guid? exceptSessionId = null,
            CancellationToken ct = default
        ) => Task.FromResult(active.Count);

        public Task AddSessionAsync(UserSession session, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<UserSession?> GetSessionByIdAsync(Guid sessionId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddTokenAsync(RefreshToken token, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<RefreshToken?> GetTokenByHashAsync(string tokenHash, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> RevokeSessionAsync(Guid sessionId, string reason, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> RevokeSurfaceTokensAsync(
            Guid sessionId,
            SessionSurface surface,
            string reason,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<bool> HasActiveChainAsync(Guid sessionId, SessionSurface surface, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingDenylist : IAccessTokenDenylist
    {
        public List<Guid> Denied { get; } = [];
        public TimeSpan Ttl { get; private set; }

        public Task DenySessionAsync(Guid sessionId, TimeSpan ttl, CancellationToken ct = default)
        {
            Denied.Add(sessionId);
            Ttl = ttl;
            return Task.CompletedTask;
        }

        public Task<bool> IsSessionDeniedAsync(Guid sessionId, CancellationToken ct = default) =>
            Task.FromResult(Denied.Contains(sessionId));
    }

    private sealed class RecordingRevocations : ISessionRevocationPublisher
    {
        public List<(Guid TenantId, Guid UserId, Guid SessionId, string Reason)> Published { get; } = [];

        public Task PublishRevokedAsync(
            Guid tenantId,
            Guid userId,
            Guid sessionId,
            string reason,
            CancellationToken ct = default
        )
        {
            Published.Add((tenantId, userId, sessionId, reason));
            return Task.CompletedTask;
        }
    }

    private sealed class OrderTrackingDenylist(List<string> order) : IAccessTokenDenylist
    {
        public Task DenySessionAsync(Guid sessionId, TimeSpan ttl, CancellationToken ct = default)
        {
            order.Add("deny");
            return Task.CompletedTask;
        }

        public Task<bool> IsSessionDeniedAsync(Guid sessionId, CancellationToken ct = default) =>
            Task.FromResult(false);
    }

    private sealed class OrderTrackingRevocations(List<string> order) : ISessionRevocationPublisher
    {
        public Task PublishRevokedAsync(
            Guid tenantId,
            Guid userId,
            Guid sessionId,
            string reason,
            CancellationToken ct = default
        )
        {
            order.Add("announce");
            return Task.CompletedTask;
        }
    }
}
