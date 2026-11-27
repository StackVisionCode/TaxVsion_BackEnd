using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.AccountSessions.Commands;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Credentials.Commands;
using TaxVision.Auth.Application.Sessions.Commands;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// Cada punto que termina sesiones publica <see cref="SessionEndedIntegrationEvent"/>: una sesión
/// (<c>SessionId</c>) o todas las del usuario (<c>SessionId</c> nulo, con la que sigue viva en <c>ExceptSessionId</c>).
/// </summary>
public sealed class SessionEndedPublishingTests
{
    private readonly AccountSessionFixture _world = new();
    private readonly FakeMessageBus _bus = new();

    [Fact]
    public async Task RevokeSession_by_the_owner_publishes_session_scope()
    {
        var (sessionId, _) = await _world.StartWorkspaceSessionAsync();

        var result = await RevokeSessionHandler.Handle(
            new RevokeSessionCommand(_world.Admin.Id, _world.Admin.TenantId, sessionId, CanManageOthers: false),
            _world.Sessions,
            _world.Users,
            _world.Denylist,
            new QuietRevocations(),
            _world.Audit,
            _world.Request,
            _world.Correlation,
            _world.UnitOfWork,
            _bus,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        AssertSessionScope(Ended(), sessionId, "user_logout");
    }

    [Fact]
    public async Task RevokeSession_by_an_admin_publishes_admin_revoke()
    {
        var (sessionId, _) = await _world.StartWorkspaceSessionAsync();

        await RevokeSessionHandler.Handle(
            new RevokeSessionCommand(
                Guid.NewGuid(),
                _world.Admin.TenantId,
                sessionId,
                CanManageOthers: true,
                RequestingActorType: UserActorType.TenantAdmin
            ),
            _world.Sessions,
            _world.Users,
            _world.Denylist,
            new QuietRevocations(),
            _world.Audit,
            _world.Request,
            _world.Correlation,
            _world.UnitOfWork,
            _bus,
            CancellationToken.None
        );

        AssertSessionScope(Ended(), sessionId, "admin_revoke");
    }

    [Fact]
    public async Task RevokeAllMySessions_publishes_user_scope_with_exception()
    {
        var (current, _) = await _world.StartWorkspaceSessionAsync();
        await StartSessionAsync(SessionSurface.Mobile);

        await RevokeAllMySessionsHandler.Handle(
            new RevokeAllMySessionsCommand(_world.Admin.Id, _world.Admin.TenantId, current),
            _world.Sessions,
            _world.Denylist,
            new QuietRevocations(),
            _world.Audit,
            _world.Request,
            _world.Correlation,
            _world.UnitOfWork,
            _bus,
            CancellationToken.None
        );

        AssertUserScope(Ended(), current, "user_logout_all");
    }

    [Fact]
    public async Task RevokeAllMySessions_with_nothing_else_open_publishes_nothing()
    {
        var (current, _) = await _world.StartWorkspaceSessionAsync();

        await RevokeAllMySessionsHandler.Handle(
            new RevokeAllMySessionsCommand(_world.Admin.Id, _world.Admin.TenantId, current),
            _world.Sessions,
            _world.Denylist,
            new QuietRevocations(),
            _world.Audit,
            _world.Request,
            _world.Correlation,
            _world.UnitOfWork,
            _bus,
            CancellationToken.None
        );

        Assert.Empty(_bus.Published.OfType<SessionEndedIntegrationEvent>());
    }

    [Fact]
    public async Task Account_only_logout_publishes_session_scope()
    {
        var account = await StartSessionAsync(SessionSurface.Account);

        await EndAccountSessionAsync(account.RefreshToken);

        AssertSessionScope(Ended(), account.SessionId, "account_logout");
    }

    [Fact]
    public async Task Account_logout_that_keeps_the_workspace_publishes_nothing()
    {
        var (sessionId, _) = await _world.StartWorkspaceSessionAsync();
        var session = (await _world.Sessions.GetSessionByIdAsync(sessionId))!;
        var account = await _world.Issuer.JoinSessionAsync(
            session,
            _world.Admin,
            "UTC",
            [],
            ["handoff"],
            SessionSurface.Account
        );

        await EndAccountSessionAsync(account.RefreshToken);

        Assert.Empty(_bus.Published.OfType<SessionEndedIntegrationEvent>());
    }

    [Fact]
    public async Task Refresh_host_mismatch_publishes_session_scope()
    {
        var (sessionId, refresh) = await _world.StartWorkspaceSessionAsync();

        await RefreshAsync(refresh, Guid.NewGuid());

        AssertSessionScope(Ended(), sessionId, "refresh_token_host_mismatch");
    }

    [Fact]
    public async Task Token_reuse_publishes_session_scope_once()
    {
        var mobile = await StartSessionAsync(SessionSurface.Mobile);
        await RefreshAsync(mobile.RefreshToken, surface: SessionSurface.Mobile, bus: new FakeMessageBus());

        await RefreshAsync(mobile.RefreshToken, surface: SessionSurface.Mobile);
        await RefreshAsync(mobile.RefreshToken, surface: SessionSurface.Mobile);

        var ended = Ended();
        AssertSessionScope(ended, mobile.SessionId, "token_reuse");
        Assert.Equal("Mobile", ended.Channel);
    }

    [Fact]
    public async Task RevokeRefreshToken_publishes_session_scope()
    {
        var (sessionId, refresh) = await _world.StartWorkspaceSessionAsync();

        await RevokeRefreshTokenHandler.Handle(
            new RevokeRefreshTokenCommand(refresh),
            _world.Sessions,
            _world.TokenService,
            _world.Denylist,
            _world.Audit,
            _world.Request,
            _world.Correlation,
            _world.UnitOfWork,
            _bus,
            CancellationToken.None
        );

        AssertSessionScope(Ended(), sessionId, "user_logout");
    }

    [Fact]
    public async Task Password_reset_publishes_user_scope()
    {
        await _world.StartWorkspaceSessionAsync();
        var credentials = new InMemoryCredentialTokenRepository();
        var rawToken = _world.TokenService.GenerateToken();
        credentials.PasswordResets.Add(
            PasswordResetToken.Create(
                _world.Admin.TenantId,
                _world.Admin.Id,
                _world.TokenService.Hash(rawToken),
                "127.0.0.1",
                TimeSpan.FromMinutes(30)
            )
        );

        var result = await ResetPasswordHandler.Handle(
            new ResetPasswordCommand(rawToken, "a-brand-new-passphrase"),
            credentials,
            _world.TokenService,
            _world.Users,
            _world.Tenants,
            new SessionAccessCutoff(_world.Sessions, _world.Denylist, new RecordingSessionRevocationPublisher()),
            new InMemoryMfaRepository(),
            new PrefixedPasswordHasher(),
            _world.Audit,
            _world.Request,
            _world.Correlation,
            _world.UnitOfWork,
            new NoUserLoginLock(),
            _bus,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        AssertUserScope(Ended(), exceptSessionId: null, "password_change");
    }

    [Fact]
    public async Task Password_change_publishes_user_scope_except_the_current_session()
    {
        var (current, _) = await _world.StartWorkspaceSessionAsync();
        await StartSessionAsync(SessionSurface.Mobile);

        var result = await ChangePasswordHandler.Handle(
            new ChangePasswordCommand(_world.Admin.Id, current, "secret", "a-brand-new-passphrase"),
            _world.Users,
            new InMemoryCredentialTokenRepository(),
            new PrefixedPasswordHasher(),
            _world.Tenants,
            new SessionAccessCutoff(_world.Sessions, _world.Denylist, new RecordingSessionRevocationPublisher()),
            _world.Audit,
            _world.Request,
            _world.Correlation,
            _world.UnitOfWork,
            _bus,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        AssertUserScope(Ended(), current, "password_change");
    }

    [Fact]
    public async Task Deactivating_a_user_publishes_user_scope()
    {
        var employee = User.Register(
            _world.Admin.TenantId,
            "Eva",
            "Employee",
            "eva@example.com",
            "hashed:secret",
            UserActorType.TenantEmployee
        ).Value;
        _world.Users.Add(employee);
        await _world.Issuer.StartSessionAsync(employee, "UTC", [], ["pwd"], null, SessionSurface.Workspace);

        var result = await DeactivateUserHandler.Handle(
            new DeactivateUserCommand(employee.TenantId, employee.Id, _world.Admin.Id, UserActorType.TenantAdmin),
            _world.Users,
            new NoTenantAdminLock(),
            new SessionAccessCutoff(_world.Sessions, _world.Denylist, new QuietRevocations()),
            _world.Audit,
            _world.Request,
            _world.Correlation,
            _world.UnitOfWork,
            _bus,
            new InMemoryCredentialTokenRepository(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var ended = Ended();
        Assert.Equal(employee.Id, ended.UserId);
        Assert.Null(ended.SessionId);
        Assert.Null(ended.ExceptSessionId);
        Assert.Equal("admin_revoke", ended.Reason);
        Assert.Equal(employee.TenantId, ended.TenantId);
    }

    private SessionEndedIntegrationEvent Ended() =>
        Assert.Single(_bus.Published.OfType<SessionEndedIntegrationEvent>());

    private void AssertSessionScope(SessionEndedIntegrationEvent ended, Guid sessionId, string reason)
    {
        Assert.Equal(sessionId, ended.SessionId);
        Assert.Null(ended.ExceptSessionId);
        Assert.Equal(reason, ended.Reason);
        Assert.Equal(_world.Admin.Id, ended.UserId);
        Assert.Equal(_world.Admin.TenantId, ended.TenantId);
    }

    private void AssertUserScope(SessionEndedIntegrationEvent ended, Guid? exceptSessionId, string reason)
    {
        Assert.Null(ended.SessionId);
        Assert.Equal(exceptSessionId, ended.ExceptSessionId);
        Assert.Equal(reason, ended.Reason);
        Assert.Equal(_world.Admin.Id, ended.UserId);
        Assert.Equal(_world.Admin.TenantId, ended.TenantId);
    }

    private Task<IssuedTokens> StartSessionAsync(SessionSurface surface) =>
        _world.Issuer.StartSessionAsync(_world.Admin, "UTC", [], ["pwd"], null, surface);

    private Task EndAccountSessionAsync(string refreshToken) =>
        EndAccountSessionHandler.Handle(
            new EndAccountSessionCommand(refreshToken),
            _world.Sessions,
            _world.TokenService,
            _world.Denylist,
            _world.Audit,
            _world.Request,
            _world.Correlation,
            _world.UnitOfWork,
            _bus,
            CancellationToken.None
        );

    private Task RefreshAsync(
        string refreshToken,
        Guid? resolvedTenantId = null,
        SessionSurface surface = SessionSurface.Workspace,
        FakeMessageBus? bus = null
    ) =>
        RefreshAccessTokenHandler.Handle(
            new RefreshAccessTokenCommand(refreshToken, resolvedTenantId, surface),
            _world.Sessions,
            _world.TokenService,
            _world.Users,
            _world.Tenants,
            _world.Roles,
            _world.Issuer,
            new FakeSessionLifetimeGate(),
            _world.Denylist,
            _world.Audit,
            _world.Request,
            _world.Correlation,
            _world.UnitOfWork,
            bus ?? _bus,
            CancellationToken.None
        );

    private sealed class QuietRevocations : ISessionRevocationPublisher
    {
        public Task PublishRevokedAsync(
            Guid tenantId,
            Guid userId,
            Guid sessionId,
            string reason,
            CancellationToken ct = default
        ) => Task.CompletedTask;
    }

    private sealed class NoTenantAdminLock : ITenantAdminLock
    {
        public Task AcquireAsync(Guid tenantId, CancellationToken ct) => Task.CompletedTask;
    }
}
