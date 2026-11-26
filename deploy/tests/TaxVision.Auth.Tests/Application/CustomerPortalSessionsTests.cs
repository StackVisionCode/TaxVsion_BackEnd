using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Customers.PortalSessions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Mfa;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Sessions;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// Sesiones de la cuenta del portal de un cliente, a pedido de Customer (que ya comprobó el permiso y la
/// visibilidad del cliente). Solo alcanzan al usuario del portal de ESE cliente.
/// </summary>
public sealed class CustomerPortalSessionsTests
{
    private readonly AccountSessionFixture _world = new();
    private readonly InMemoryMfaRepository _mfa = new();
    private readonly CountingRevocations _revocations = new();
    private readonly FakeMessageBus _bus = new();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly User _client;

    public CustomerPortalSessionsTests()
    {
        _client = User.Register(
            _world.Tenant.Id,
            "Minato",
            "Kenyimura",
            "minato@example.com",
            "hashed:x",
            UserActorType.CustomerPortal,
            _customerId
        ).Value;
        _world.Users.Add(_client);
    }

    [Fact]
    public async Task A_client_without_portal_access_has_no_sessions_to_show()
    {
        var history = new RecordingHistory();

        var result = await GetCustomerPortalSessionsHandler.Handle(
            new GetCustomerPortalSessionsQuery(_world.Tenant.Id, Guid.NewGuid()),
            _world.Users,
            history,
            new OpenGate(),
            CancellationToken.None
        );

        Assert.Equal("Auth.NoPortalAccount", result.Error.Code);
        Assert.Null(history.UserId);
    }

    [Fact]
    public async Task The_list_reads_only_the_portal_user_of_that_client()
    {
        var history = new RecordingHistory();

        var result = await GetCustomerPortalSessionsHandler.Handle(
            new GetCustomerPortalSessionsQuery(_world.Tenant.Id, _customerId),
            _world.Users,
            history,
            new OpenGate(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(_client.Id, history.UserId);
        Assert.Equal(_world.Tenant.Id, history.TenantId);
        Assert.Equal("Minato Kenyimura", Assert.Single(result.Value.Items).UserDisplayName);
    }

    [Fact]
    public async Task Signing_out_one_session_records_the_office_member_who_asked()
    {
        var session = Open(_client);

        var result = await RevokeOne(session.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal("admin_revoke", session.RevokedReason);
        Assert.Equal(_world.Admin.Id, session.RevokedByUserId);
        Assert.Equal([session.Id], _world.Denylist.Denied);
        Assert.Equal([session.Id], _revocations.Published);
        var log = Assert.Single(_world.Audit.Logs);
        Assert.Equal(AuthAuditAction.SessionRevoked, log.Action);
        Assert.Equal(_world.Admin.Id, log.UserId);
        Assert.Contains(_client.Id.ToString(), log.DetailsJson);
        Assert.Single(_bus.Published.OfType<SessionEndedIntegrationEvent>());
    }

    [Fact]
    public async Task A_session_of_someone_else_is_not_found()
    {
        var staffSession = Open(_world.Admin);

        var result = await RevokeOne(staffSession.Id);

        Assert.Equal("Session.NotFound", result.Error.Code);
        Assert.True(staffSession.IsActive);
    }

    [Fact]
    public async Task Signing_out_everywhere_cuts_each_portal_session()
    {
        var web = Open(_client);
        var mobile = Open(_client, SessionSurface.Mobile);
        _mfa.Devices.Add(TrustedDevice.Create(_client.TenantId, _client.Id, "hash", "Chrome", TimeSpan.FromDays(30)));

        var result = await RevokeAll(forgetTrustedDevices: true);

        Assert.True(result.IsSuccess);
        Assert.All([web, mobile], session => Assert.Equal("admin_revoke", session.RevokedReason));
        Assert.All([web, mobile], session => Assert.Equal(_world.Admin.Id, session.RevokedByUserId));
        Assert.Equal(2, _revocations.Published.Count);
        Assert.Equal(2, _bus.Published.OfType<SessionEndedIntegrationEvent>().Count());
        Assert.All(_mfa.Devices, device => Assert.False(device.IsActive));
    }

    [Fact]
    public async Task Signing_out_everywhere_without_portal_access_fails_clearly()
    {
        var result = await RevokeCustomerPortalSessionsHandler.Handle(
            new RevokeCustomerPortalSessionsCommand(_world.Tenant.Id, Guid.NewGuid(), _world.Admin.Id, false, "c"),
            _world.Users,
            _world.Sessions,
            new SessionAccessCutoff(_world.Sessions, _world.Denylist, _revocations),
            _mfa,
            _world.Audit,
            _world.UnitOfWork,
            _bus,
            CancellationToken.None
        );

        Assert.Equal("Auth.NoPortalAccount", result.Error.Code);
    }

    private UserSession Open(User user, SessionSurface surface = SessionSurface.Account)
    {
        var session = UserSession.Start(user.TenantId, user.Id, "Chrome", "agent", "203.0.113.10", surface);
        _world.Sessions.Sessions.Add(session);
        return session;
    }

    private Task<BuildingBlocks.Results.Result> RevokeOne(Guid sessionId) =>
        RevokeCustomerPortalSessionHandler.Handle(
            new RevokeCustomerPortalSessionCommand(_world.Tenant.Id, _customerId, sessionId, _world.Admin.Id, "c"),
            _world.Users,
            _world.Sessions,
            _world.Denylist,
            _revocations,
            _world.Audit,
            _world.UnitOfWork,
            _bus,
            CancellationToken.None
        );

    private Task<BuildingBlocks.Results.Result> RevokeAll(bool forgetTrustedDevices) =>
        RevokeCustomerPortalSessionsHandler.Handle(
            new RevokeCustomerPortalSessionsCommand(
                _world.Tenant.Id,
                _customerId,
                _world.Admin.Id,
                forgetTrustedDevices,
                "c"
            ),
            _world.Users,
            _world.Sessions,
            new SessionAccessCutoff(_world.Sessions, _world.Denylist, _revocations),
            _mfa,
            _world.Audit,
            _world.UnitOfWork,
            _bus,
            CancellationToken.None
        );

    private sealed class CountingRevocations : ISessionRevocationPublisher
    {
        public List<Guid> Published { get; } = [];

        public Task PublishRevokedAsync(
            Guid tenantId,
            Guid userId,
            Guid sessionId,
            string reason,
            CancellationToken ct = default
        )
        {
            Published.Add(sessionId);
            return Task.CompletedTask;
        }
    }

    private sealed class OpenGate : ISessionLifetimeGate
    {
        public Task<string?> EndReasonAsync(UserSession session, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);

        public Task<SessionActivityWindow> ActivityWindowAsync(CancellationToken ct = default) =>
            Task.FromResult(new SessionActivityWindow(DateTime.MinValue, DateTime.MinValue));

        public void Invalidate() { }
    }

    private sealed class RecordingHistory : ISessionHistory
    {
        public Guid? TenantId { get; private set; }
        public Guid? UserId { get; private set; }

        public Task<SessionHistoryPage> ListForUserAsync(
            Guid tenantId,
            Guid userId,
            DateTime fromUtc,
            DateTime toUtc,
            int page,
            int pageSize,
            CancellationToken ct = default
        )
        {
            TenantId = tenantId;
            UserId = userId;
            var row = new SessionHistoryRow(
                Guid.NewGuid(),
                userId,
                "Minato",
                "Kenyimura",
                UserActorType.CustomerPortal,
                "Chrome",
                "agent",
                "203.0.113.10",
                "203.0.113.10",
                SessionChannel.Web,
                DateTime.UtcNow,
                DateTime.UtcNow,
                null,
                null
            );
            return Task.FromResult(new SessionHistoryPage([row], 1));
        }

        public Task<SessionHistoryPage> ListAsync(SessionHistoryFilter filter, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SessionHistoryCounts> CountAsync(
            SessionHistoryScope scope,
            DateTime fromUtc,
            DateTime toUtc,
            SessionActivityWindow window,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<int> PurgeClosedBeforeAsync(
            DateTime closedBeforeUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}
