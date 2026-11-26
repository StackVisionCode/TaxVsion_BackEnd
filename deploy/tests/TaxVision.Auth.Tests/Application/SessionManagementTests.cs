using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Sessions.Commands;
using TaxVision.Auth.Application.Sessions.Queries;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Mfa;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Sessions;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// Gestión de sesiones del equipo: quién puede cerrar qué sesión, "cerrar todas" de un miembro y las
/// consultas que alimentan la pantalla. Las reglas viven en el backend, no en el front.
/// </summary>
public sealed class SessionManagementTests
{
    private readonly AccountSessionFixture _world = new();
    private readonly InMemoryMfaRepository _mfa = new();
    private readonly CountingRevocations _revocations = new();
    private readonly FakeMessageBus _bus = new();

    // ---------------------------------------------------------------- revocar una

    [Fact]
    public async Task I_cannot_sign_out_my_current_session_from_here()
    {
        var current = Open(_world.Admin);

        var result = await Revoke(_world.Admin, current.Id, current: current.Id);

        Assert.Equal("Session.CurrentSessionNotRevocable", result.Error.Code);
        Assert.True(current.IsActive);
    }

    [Fact]
    public async Task I_can_still_sign_out_another_session_of_mine()
    {
        var current = Open(_world.Admin);
        var other = Open(_world.Admin);

        var result = await Revoke(_world.Admin, other.Id, current: current.Id, canManage: false);

        Assert.True(result.IsSuccess);
        Assert.Equal("user_logout", other.RevokedReason);
        Assert.Null(other.RevokedByUserId);
    }

    [Fact]
    public async Task A_manager_signs_out_an_employee_and_is_recorded_as_the_revoker()
    {
        var manager = AddStaff(UserActorType.TenantEmployee);
        var employee = AddStaff(UserActorType.TenantEmployee);
        var session = Open(employee);

        var result = await Revoke(manager, session.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal("admin_revoke", session.RevokedReason);
        Assert.Equal(manager.Id, session.RevokedByUserId);
        var log = Assert.Single(_world.Audit.Logs);
        Assert.Equal(AuthAuditAction.SessionRevoked, log.Action);
        Assert.Contains(employee.Id.ToString(), log.DetailsJson);
    }

    [Fact]
    public async Task Without_users_manage_someone_elses_session_is_forbidden()
    {
        var employee = AddStaff(UserActorType.TenantEmployee);
        var session = Open(employee);

        var result = await Revoke(AddStaff(UserActorType.TenantEmployee), session.Id, canManage: false);

        Assert.Equal("Session.Forbidden", result.Error.Code);
    }

    [Fact]
    public async Task An_employee_manager_cannot_sign_out_an_admin()
    {
        var session = Open(_world.Admin);

        var result = await Revoke(AddStaff(UserActorType.TenantEmployee), session.Id);

        Assert.Equal("Session.Forbidden", result.Error.Code);
        Assert.True(session.IsActive);
    }

    [Fact]
    public async Task An_admin_can_sign_out_another_admin()
    {
        var other = AddStaff(UserActorType.TenantAdmin);
        var session = Open(other);

        var result = await Revoke(_world.Admin, session.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal("admin_revoke", session.RevokedReason);
    }

    [Fact]
    public async Task A_portal_account_is_not_reachable_from_team_management()
    {
        var session = Open(AddPortal());

        var result = await Revoke(_world.Admin, session.Id);

        Assert.Equal("Session.NotFound", result.Error.Code);
        Assert.True(session.IsActive);
    }

    [Fact]
    public async Task Another_office_session_is_not_found()
    {
        var foreign = User.Register(
            Guid.NewGuid(),
            "Bo",
            "Other",
            "bo@example.com",
            "hashed:x",
            UserActorType.TenantEmployee
        ).Value;
        _world.Users.Add(foreign);
        var session = Open(foreign);

        var result = await Revoke(_world.Admin, session.Id);

        Assert.Equal("Session.NotFound", result.Error.Code);
    }

    // ---------------------------------------------------------------- revocar todas

    [Fact]
    public async Task I_cannot_sign_myself_out_from_team_management()
    {
        Open(_world.Admin);

        var result = await RevokeAll(_world.Admin, _world.Admin.Id);

        Assert.Equal("Session.SelfRevokeNotAllowed", result.Error.Code);
        Assert.Empty(_world.Denylist.Denied);
    }

    [Fact]
    public async Task Signing_out_everywhere_cuts_each_session_and_announces_each_one()
    {
        var employee = AddStaff(UserActorType.TenantEmployee);
        var web = Open(employee);
        var mobile = Open(employee, SessionSurface.Mobile);

        var result = await RevokeAll(_world.Admin, employee.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { web.Id, mobile.Id }.Order(), _world.Denylist.Denied.Order());
        Assert.Equal(2, _revocations.Published.Count);
        Assert.All([web, mobile], session => Assert.Equal("admin_revoke", session.RevokedReason));
        Assert.All([web, mobile], session => Assert.Equal(_world.Admin.Id, session.RevokedByUserId));
        var ended = _bus.Published.OfType<SessionEndedIntegrationEvent>().ToList();
        Assert.Equal(2, ended.Count);
        Assert.All(ended, evt => Assert.Equal("admin_revoke", evt.Reason));
    }

    [Fact]
    public async Task An_employee_manager_cannot_sign_out_an_admin_everywhere()
    {
        var other = AddStaff(UserActorType.TenantAdmin);
        Open(other);

        var result = await RevokeAll(AddStaff(UserActorType.TenantEmployee), other.Id);

        Assert.Equal("Session.Forbidden", result.Error.Code);
        Assert.Empty(_world.Denylist.Denied);
    }

    [Fact]
    public async Task A_portal_account_or_another_office_is_user_not_found()
    {
        var portal = AddPortal();
        var foreign = User.Register(
            Guid.NewGuid(),
            "Bo",
            "Other",
            "bo@example.com",
            "hashed:x",
            UserActorType.TenantEmployee
        ).Value;
        _world.Users.Add(foreign);

        Assert.Equal("User.NotFound", (await RevokeAll(_world.Admin, portal.Id)).Error.Code);
        Assert.Equal("User.NotFound", (await RevokeAll(_world.Admin, foreign.Id)).Error.Code);
        Assert.Equal("User.NotFound", (await RevokeAll(_world.Admin, Guid.NewGuid())).Error.Code);
    }

    [Fact]
    public async Task Forgetting_trusted_devices_removes_them()
    {
        var employee = AddStaff(UserActorType.TenantEmployee);
        _mfa.Devices.Add(TrustedDevice.Create(employee.TenantId, employee.Id, "hash", "Chrome", TimeSpan.FromDays(30)));

        await RevokeAll(_world.Admin, employee.Id, forgetTrustedDevices: true);

        Assert.All(_mfa.Devices, device => Assert.False(device.IsActive));
    }

    [Fact]
    public async Task Trusted_devices_stay_unless_asked()
    {
        var employee = AddStaff(UserActorType.TenantEmployee);
        _mfa.Devices.Add(TrustedDevice.Create(employee.TenantId, employee.Id, "hash", "Chrome", TimeSpan.FromDays(30)));

        await RevokeAll(_world.Admin, employee.Id);

        Assert.All(_mfa.Devices, device => Assert.True(device.IsActive));
    }

    [Fact]
    public async Task A_member_without_sessions_is_a_quiet_success()
    {
        var employee = AddStaff(UserActorType.TenantEmployee);

        var result = await RevokeAll(_world.Admin, employee.Id);

        Assert.True(result.IsSuccess);
        Assert.Empty(_revocations.Published);
        Assert.Empty(_bus.Published.OfType<SessionEndedIntegrationEvent>());
    }

    // ---------------------------------------------------------------- consultas

    [Fact]
    public async Task My_sessions_mark_the_current_one_and_keep_the_sign_in_ip()
    {
        var current = Open(_world.Admin);
        var other = Open(_world.Admin);

        var result = await GetMySessionsHandler.Handle(
            new GetMySessionsQuery(_world.Admin.Id, current.Id),
            _world.Sessions,
            new DefaultPolicyGate(),
            CancellationToken.None
        );

        var rows = result.Value;
        Assert.True(rows.Single(row => row.Id == current.Id).IsCurrent);
        Assert.False(rows.Single(row => row.Id == other.Id).IsCurrent);
        Assert.All(rows, row => Assert.Equal("Active", row.Status));
        Assert.All(rows, row => Assert.Equal("203.0.113.10", row.StartIpAddress));
        Assert.All(rows, row => Assert.Null(row.Location));
    }

    [Fact]
    public async Task An_employee_manager_cannot_list_an_admins_sessions()
    {
        var result = await UserSessions(AddStaff(UserActorType.TenantEmployee), _world.Admin.Id);

        Assert.Equal("Session.Forbidden", result.Error.Code);
    }

    [Fact]
    public async Task A_portal_account_sessions_are_not_listed_for_the_team()
    {
        var result = await UserSessions(_world.Admin, AddPortal().Id);

        Assert.Equal("User.NotFound", result.Error.Code);
    }

    [Fact]
    public void The_office_period_defaults_to_a_week_and_never_exceeds_ninety_days()
    {
        var now = new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal((now.AddDays(-7), now), SessionPeriod.Resolve(null, null, now));
        Assert.Equal((now.AddDays(-90), now), SessionPeriod.Resolve(now.AddDays(-400), null, now));
        Assert.Equal(100, SessionPeriod.PageSize(500));
        Assert.Equal(20, SessionPeriod.PageSize(null));
    }

    [Fact]
    public void A_huge_page_number_is_capped_instead_of_overflowing()
    {
        Assert.Equal(1, SessionPeriod.Page(null));
        Assert.Equal(1, SessionPeriod.Page(0));
        Assert.Equal(SessionPeriod.MaxPage, SessionPeriod.Page(int.MaxValue));
    }

    [Fact]
    public async Task Sign_ins_per_day_are_cut_in_the_office_time_zone()
    {
        // 03:30 UTC del 25 es todavía el 24 en Nueva York.
        var history = new FixedHistory([
            new DateTime(2026, 11, 25, 3, 30, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 25, 15, 0, 0, DateTimeKind.Utc),
        ]);

        var result = await GetSessionSummaryHandler.Handle(
            new GetSessionSummaryQuery(
                _world.Tenant.Id,
                UserActorType.TenantAdmin,
                new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 11, 25, 20, 0, 0, DateTimeKind.Utc)
            ),
            history,
            new DefaultPolicyGate(),
            _world.Tenants,
            CancellationToken.None
        );

        var days = result.Value.SignInsPerDay.ToDictionary(day => day.Date, day => day.Count);
        Assert.Equal(1, days[new DateOnly(2026, 11, 24)]);
        Assert.Equal(1, days[new DateOnly(2026, 11, 25)]);
        Assert.Equal(2, result.Value.SignInsInPeriod);
    }

    // ---------------------------------------------------------------- ubicación

    [Fact]
    public async Task Without_the_location_permission_the_location_is_hidden()
    {
        var employee = AddStaff(UserActorType.TenantEmployee);
        Open(employee, location: MountainView);

        var row = Assert.Single((await UserSessions(_world.Admin, employee.Id, canSeeLocation: false)).Value);

        Assert.Null(row.Location);
        Assert.True(row.LocationHidden);
    }

    [Fact]
    public async Task With_the_location_permission_the_location_is_shown()
    {
        var employee = AddStaff(UserActorType.TenantEmployee);
        Open(employee, location: MountainView);

        var row = Assert.Single((await UserSessions(_world.Admin, employee.Id, canSeeLocation: true)).Value);

        Assert.Equal("US", row.Location!.CountryCode);
        Assert.Equal("Mountain View", row.Location.City);
        Assert.False(row.LocationHidden);
    }

    [Fact]
    public async Task My_own_sessions_always_show_their_location()
    {
        Open(_world.Admin, location: MountainView);

        var result = await GetMySessionsHandler.Handle(
            new GetMySessionsQuery(_world.Admin.Id),
            _world.Sessions,
            new DefaultPolicyGate(),
            CancellationToken.None
        );

        Assert.Equal("US", Assert.Single(result.Value).Location!.CountryCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Countries_in_the_summary_need_the_location_permission(bool canSeeLocation)
    {
        var result = await GetSessionSummaryHandler.Handle(
            new GetSessionSummaryQuery(_world.Tenant.Id, UserActorType.TenantAdmin, CanSeeLocation: canSeeLocation),
            new FixedHistory([], [new SessionCountryCount("US", 3)], withoutLocation: 2),
            new DefaultPolicyGate(),
            _world.Tenants,
            CancellationToken.None
        );

        if (canSeeLocation)
        {
            Assert.Equal(3, Assert.Single(result.Value.ByCountry!).Count);
            Assert.Equal(2, result.Value.WithoutLocation);
        }
        else
        {
            Assert.Null(result.Value.ByCountry);
            Assert.Null(result.Value.WithoutLocation);
        }
    }

    // ---------------------------------------------------------------- ayudas

    private static readonly SessionLocation MountainView = SessionLocation
        .Create("US", "California", "Mountain View", 37.42, -122.08, DateTime.UtcNow)
        .Value;

    private User AddStaff(UserActorType actorType)
    {
        var user = User.Register(
            _world.Tenant.Id,
            "Sam",
            actorType.ToString(),
            $"{Guid.NewGuid():N}@example.com",
            "hashed:x",
            actorType
        ).Value;
        _world.Users.Add(user);
        return user;
    }

    private User AddPortal()
    {
        var user = User.Register(
            _world.Tenant.Id,
            "Cy",
            "Client",
            $"{Guid.NewGuid():N}@example.com",
            "hashed:x",
            UserActorType.CustomerPortal,
            Guid.NewGuid()
        ).Value;
        _world.Users.Add(user);
        return user;
    }

    private UserSession Open(
        User user,
        SessionSurface surface = SessionSurface.Workspace,
        SessionLocation? location = null
    )
    {
        var session = UserSession.Start(user.TenantId, user.Id, "Chrome", "agent", "203.0.113.10", surface, location);
        _world.Sessions.Sessions.Add(session);
        return session;
    }

    private Task<Result> Revoke(User requester, Guid sessionId, Guid? current = null, bool canManage = true) =>
        RevokeSessionHandler.Handle(
            new RevokeSessionCommand(
                requester.Id,
                requester.TenantId,
                sessionId,
                canManage,
                current ?? Guid.NewGuid(),
                requester.ActorType
            ),
            _world.Sessions,
            _world.Users,
            _world.Denylist,
            _revocations,
            _world.Audit,
            _world.Request,
            _world.Correlation,
            _world.UnitOfWork,
            _bus,
            CancellationToken.None
        );

    private Task<Result> RevokeAll(User requester, Guid targetUserId, bool forgetTrustedDevices = false) =>
        RevokeUserSessionsHandler.Handle(
            new RevokeUserSessionsCommand(
                requester.Id,
                requester.TenantId,
                requester.ActorType,
                targetUserId,
                forgetTrustedDevices
            ),
            _world.Users,
            _world.Sessions,
            new SessionAccessCutoff(_world.Sessions, _world.Denylist, _revocations),
            _mfa,
            _world.Audit,
            _world.Request,
            _world.Correlation,
            _world.UnitOfWork,
            _bus,
            CancellationToken.None
        );

    private Task<Result<IReadOnlyList<SessionResponse>>> UserSessions(
        User requester,
        Guid targetUserId,
        bool canSeeLocation = false
    ) =>
        GetUserSessionsHandler.Handle(
            new GetUserSessionsQuery(
                requester.TenantId,
                targetUserId,
                requester.ActorType,
                CanSeeLocation: canSeeLocation,
                RequestingUserId: requester.Id
            ),
            _world.Users,
            _world.Sessions,
            new DefaultPolicyGate(),
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

    /// <summary>La política por defecto, como la aplica el gate real.</summary>
    private sealed class DefaultPolicyGate : ISessionLifetimeGate
    {
        private static readonly TimeSpan Refresh = TimeSpan.FromMinutes(15);

        public Task<string?> EndReasonAsync(UserSession session, CancellationToken ct = default) =>
            Task.FromResult(
                PlatformSessionPolicy.CreateDefault(DateTime.UtcNow).EndReason(session, Refresh, DateTime.UtcNow)
            );

        public Task<SessionActivityWindow> ActivityWindowAsync(CancellationToken ct = default) =>
            Task.FromResult(
                PlatformSessionPolicy.CreateDefault(DateTime.UtcNow).ActivityWindow(Refresh, DateTime.UtcNow)
            );

        public void Invalidate() { }
    }

    private sealed class FixedHistory(
        IReadOnlyList<DateTime> signIns,
        IReadOnlyList<SessionCountryCount>? byCountry = null,
        int withoutLocation = 0
    ) : ISessionHistory
    {
        public Task<SessionHistoryPage> ListAsync(SessionHistoryFilter filter, CancellationToken ct = default) =>
            Task.FromResult(new SessionHistoryPage([], 0));

        public Task<SessionHistoryCounts> CountAsync(
            SessionHistoryScope scope,
            DateTime fromUtc,
            DateTime toUtc,
            SessionActivityWindow window,
            CancellationToken ct = default
        ) => Task.FromResult(new SessionHistoryCounts(0, 0, 0, 0, signIns, byCountry ?? [], withoutLocation));

        public Task<int> PurgeClosedBeforeAsync(
            DateTime closedBeforeUtc,
            int batchSize,
            CancellationToken ct = default
        ) => Task.FromResult(0);

        public Task<SessionHistoryPage> ListForUserAsync(
            Guid tenantId,
            Guid userId,
            DateTime fromUtc,
            DateTime toUtc,
            int page,
            int pageSize,
            CancellationToken ct = default
        ) => Task.FromResult(new SessionHistoryPage([], 0));
    }
}
