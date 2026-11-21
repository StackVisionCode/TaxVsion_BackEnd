using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Users;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Sessions;
using TaxVision.Auth.Domain.Users;
using Xunit;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Fase 18.3 — RefreshToken host binding: un token emitido para un tenant no debe canjearse
/// desde el subdominio de otro. Además, cada superficie de la sesión (workspace y Account) rota su cadena.</summary>
public sealed class RefreshAccessTokenHandlerTests
{
    [Fact]
    public async Task Host_mismatch_revokes_the_session_and_publishes_a_security_alert()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var stored = RefreshToken.Create(tenantId, userId, sessionId, "hash", DateTime.UtcNow.AddDays(1));

        var sessions = new FakeSessionRepositoryForRefresh(stored);
        var denylist = new FakeAccessTokenDenylist();
        var bus = new FakeMessageBus();

        var result = await RefreshAccessTokenHandler.Handle(
            new RefreshAccessTokenCommand("raw-token", otherTenantId),
            sessions,
            new FakeSecureTokenService(),
            new ExplodingUserRepository(),
            new ExplodingTenantRegistry(),
            new ExplodingRoleRepository(),
            new ExplodingAuthSessionIssuer(),
            denylist,
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Auth.InvalidRefreshToken", result.Error.Code);
        Assert.Equal(sessionId, sessions.RevokedSessionId);
        Assert.Equal(sessionId, denylist.DeniedSessionId);
        var alert = Assert.IsType<SecurityAlertIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(SecurityAlertType.RefreshTokenHostMismatch, alert.AlertType);
    }

    [Fact]
    public async Task Matching_resolved_tenant_does_not_trigger_the_mismatch_path()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var stored = RefreshToken.Create(tenantId, userId, sessionId, "hash", DateTime.UtcNow.AddDays(1));

        var sessions = new FakeSessionRepositoryForRefresh(stored);
        var denylist = new FakeAccessTokenDenylist();

        var result = await RefreshAccessTokenHandler.Handle(
            new RefreshAccessTokenCommand("raw-token", tenantId),
            sessions,
            new FakeSecureTokenService(),
            new ExplodingUserRepository(),
            new ExplodingTenantRegistry(),
            new ExplodingRoleRepository(),
            new ExplodingAuthSessionIssuer(),
            denylist,
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Auth.InvalidRefreshToken", result.Error.Code);
        Assert.Null(sessions.RevokedSessionId);
        Assert.Null(denylist.DeniedSessionId);
    }

    [Fact]
    public async Task Each_surface_rotates_its_own_chain_without_tripping_reuse_detection_on_the_other()
    {
        var world = new AccountSessionFixture();
        var (sessionId, workspaceRefresh) = await world.StartWorkspaceSessionAsync();
        var session = (await world.Sessions.GetSessionByIdAsync(sessionId))!;
        var account = await world.Issuer.JoinSessionAsync(
            session,
            world.Admin,
            "UTC",
            [],
            ["handoff"],
            SessionSurface.Account
        );

        var workspace = await RefreshAsync(world, workspaceRefresh, SessionSurface.Workspace);
        var accountRotated = await RefreshAsync(world, account.RefreshToken, SessionSurface.Account);
        var workspaceAgain = await RefreshAsync(world, workspace.Value.RefreshToken, SessionSurface.Workspace);

        Assert.True(workspace.IsSuccess && accountRotated.IsSuccess && workspaceAgain.IsSuccess);
        Assert.True(session.IsActive);
        // Emisiones: inicio CRM, alta Account, refresh CRM, refresh Account, refresh CRM.
        Assert.Equal(
            new[]
            {
                SessionSurface.Workspace,
                SessionSurface.Account,
                SessionSurface.Workspace,
                SessionSurface.Account,
                SessionSurface.Workspace,
            },
            world.Jwt.Issued.Select(issued => issued.Surface)
        );
        var rotated = await world.Sessions.GetTokenByHashAsync(
            world.TokenService.Hash(accountRotated.Value.RefreshToken)
        );
        Assert.Equal(SessionSurface.Account, rotated!.Surface);
    }

    [Fact]
    public async Task A_refresh_endpoint_only_accepts_tokens_of_its_own_surface()
    {
        var world = new AccountSessionFixture();
        var (sessionId, workspaceRefresh) = await world.StartWorkspaceSessionAsync();

        var result = await RefreshAsync(world, workspaceRefresh, SessionSurface.Account);

        Assert.Equal("Auth.InvalidRefreshToken", result.Error.Code);
        Assert.True((await world.Sessions.GetSessionByIdAsync(sessionId))!.IsActive);
    }

    // La ruta móvil no compara host: un token del CRM o del portal no puede renovarse por ahí (binding de host).
    [Fact]
    public async Task A_web_token_is_never_renewed_through_the_mobile_route()
    {
        var world = new AccountSessionFixture();
        var (sessionId, workspaceRefresh) = await world.StartWorkspaceSessionAsync();

        var result = await RefreshAsync(world, workspaceRefresh, SessionSurface.Mobile);

        Assert.Equal("Auth.InvalidRefreshToken", result.Error.Code);
        Assert.True((await world.Sessions.GetSessionByIdAsync(sessionId))!.IsActive);
    }

    [Fact]
    public async Task A_mobile_token_rotates_on_the_mobile_route_and_never_on_the_web_one()
    {
        var world = new AccountSessionFixture();
        var mobile = await world.Issuer.StartSessionAsync(
            world.Admin,
            "UTC",
            [],
            ["pwd"],
            "iPhone",
            SessionSurface.Mobile
        );

        var onWeb = await RefreshAsync(world, mobile.RefreshToken, SessionSurface.Workspace);
        var onMobile = await RefreshAsync(world, mobile.RefreshToken, SessionSurface.Mobile);

        Assert.Equal("Auth.InvalidRefreshToken", onWeb.Error.Code);
        Assert.True(onMobile.IsSuccess);
    }

    // Señal de uso indebido: queda rastro, pero ni la respuesta cambia ni se cierra la sesión.
    [Fact]
    public async Task A_web_token_on_the_mobile_route_leaves_a_trace_and_keeps_the_session()
    {
        var world = new AccountSessionFixture();
        var (sessionId, workspaceRefresh) = await world.StartWorkspaceSessionAsync();

        var result = await RefreshAsync(world, workspaceRefresh, SessionSurface.Mobile);

        Assert.Equal("Auth.InvalidRefreshToken", result.Error.Code);
        var log = Assert.Single(world.Audit.Logs);
        Assert.Equal(AuthAuditAction.RefreshTokenSurfaceMismatch, log.Action);
        Assert.False(log.Success);
        Assert.Equal(world.Admin.Id, log.UserId);
        Assert.Equal(world.Tenant.Id, log.TenantId);
        Assert.Equal(sessionId, log.TargetId);
        Assert.Equal("""{"tokenSurface":"Workspace","route":"Mobile"}""", log.DetailsJson);
        Assert.Equal(1, world.UnitOfWork.SaveChangesCallCount);
        Assert.True((await world.Sessions.GetSessionByIdAsync(sessionId))!.IsActive);
        Assert.Empty(world.Denylist.Denied);
        Assert.True((await RefreshAsync(world, workspaceRefresh, SessionSurface.Workspace)).IsSuccess);
    }

    [Fact]
    public async Task A_mobile_token_on_the_web_route_leaves_a_trace_and_keeps_the_session()
    {
        var world = new AccountSessionFixture();
        var mobile = await world.Issuer.StartSessionAsync(
            world.Admin,
            "UTC",
            [],
            ["pwd"],
            "iPhone",
            SessionSurface.Mobile
        );

        var result = await RefreshAsync(world, mobile.RefreshToken, SessionSurface.Workspace);

        Assert.Equal("Auth.InvalidRefreshToken", result.Error.Code);
        var log = Assert.Single(world.Audit.Logs);
        Assert.Equal(AuthAuditAction.RefreshTokenSurfaceMismatch, log.Action);
        Assert.Equal("""{"tokenSurface":"Mobile","route":"Workspace"}""", log.DetailsJson);
        Assert.True((await world.Sessions.GetSessionByIdAsync(mobile.SessionId))!.IsActive);
        Assert.Empty(world.Denylist.Denied);
        Assert.True((await RefreshAsync(world, mobile.RefreshToken, SessionSurface.Mobile)).IsSuccess);
    }

    [Fact]
    public async Task An_unknown_token_leaves_no_trace()
    {
        var world = new AccountSessionFixture();

        var result = await RefreshAsync(world, "never-issued", SessionSurface.Mobile);

        Assert.Equal("Auth.InvalidRefreshToken", result.Error.Code);
        Assert.Empty(world.Audit.Logs);
        Assert.Equal(0, world.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Revoking_the_shared_session_cuts_both_surfaces()
    {
        var world = new AccountSessionFixture();
        var (sessionId, workspaceRefresh) = await world.StartWorkspaceSessionAsync();
        var session = (await world.Sessions.GetSessionByIdAsync(sessionId))!;
        var account = await world.Issuer.JoinSessionAsync(
            session,
            world.Admin,
            "UTC",
            [],
            ["handoff"],
            SessionSurface.Account
        );

        await world.Sessions.RevokeSessionAsync(sessionId, "single_session_superseded");

        Assert.True((await RefreshAsync(world, workspaceRefresh, SessionSurface.Workspace)).IsFailure);
        Assert.True((await RefreshAsync(world, account.RefreshToken, SessionSurface.Account)).IsFailure);
    }

    // Un cliente en bucle reintentando un token ya rotado: la sesión se revoca una vez y la alerta sale una vez.
    [Fact]
    public async Task Retrying_a_reused_token_alerts_only_once_per_session()
    {
        var world = new AccountSessionFixture();
        var mobile = await world.Issuer.StartSessionAsync(
            world.Admin,
            "UTC",
            [],
            ["pwd"],
            "iPhone",
            SessionSurface.Mobile
        );
        var rotated = await RefreshAsync(world, mobile.RefreshToken, SessionSurface.Mobile);
        var bus = new FakeMessageBus();

        await RefreshAsync(world, mobile.RefreshToken, SessionSurface.Mobile, bus);
        await RefreshAsync(world, mobile.RefreshToken, SessionSurface.Mobile, bus);
        await RefreshAsync(world, mobile.RefreshToken, SessionSurface.Mobile, bus);

        Assert.True(rotated.IsSuccess);
        var alert = Assert.IsType<SecurityAlertIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(SecurityAlertType.TokenReuseDetected, alert.AlertType);
        Assert.False((await world.Sessions.GetSessionByIdAsync(mobile.SessionId))!.IsActive);
    }

    private static Task<BuildingBlocks.Results.Result<AuthTokensResponse>> RefreshAsync(
        AccountSessionFixture world,
        string refreshToken,
        SessionSurface surface,
        FakeMessageBus? bus = null
    ) =>
        RefreshAccessTokenHandler.Handle(
            new RefreshAccessTokenCommand(refreshToken, ResolvedTenantId: null, surface),
            world.Sessions,
            world.TokenService,
            world.Users,
            world.Tenants,
            world.Roles,
            world.Issuer,
            world.Denylist,
            world.Audit,
            world.Request,
            world.Correlation,
            world.UnitOfWork,
            bus ?? new FakeMessageBus(),
            CancellationToken.None
        );

    private sealed class FakeSecureTokenService : ISecureTokenService
    {
        public string GenerateToken(int byteLength = 32) => "raw-token";

        public string GenerateNumericCode(int digits = 6) => "123456";

        public string Hash(string rawToken) => "hash";
    }

    /// <summary>Sesión inactiva (null) tras el chequeo de mismatch — el handler corta ahí con
    /// Auth.InvalidRefreshToken sin necesitar alcanzar users/tenants/roles/issuer.</summary>
    private sealed class FakeSessionRepositoryForRefresh(RefreshToken stored) : ISessionRepository
    {
        public Task<IReadOnlyList<UserSession>> GetActiveSessionsByTenantAsync(
            Guid tenantId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<UserSession>>([]);

        public Guid? RevokedSessionId { get; private set; }

        public Task AddSessionAsync(UserSession session, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<UserSession?> GetSessionByIdAsync(Guid sessionId, CancellationToken ct = default) =>
            Task.FromResult<UserSession?>(null);

        public Task<IReadOnlyList<UserSession>> GetActiveSessionsByUserAsync(
            Guid userId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task AddTokenAsync(RefreshToken token, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<RefreshToken?> GetTokenByHashAsync(string tokenHash, CancellationToken ct = default) =>
            Task.FromResult<RefreshToken?>(stored);

        public Task<int> RevokeSessionAsync(Guid sessionId, string reason, CancellationToken ct = default)
        {
            RevokedSessionId = sessionId;
            return Task.FromResult(1);
        }

        public Task<int> RevokeAllForUserAsync(
            Guid userId,
            string reason,
            Guid? exceptSessionId = null,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<int> RevokeSurfaceTokensAsync(
            Guid sessionId,
            SessionSurface surface,
            string reason,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<bool> HasActiveChainAsync(Guid sessionId, SessionSurface surface, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> RevokeAllForTenantAsync(Guid tenantId, string reason, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeAccessTokenDenylist : IAccessTokenDenylist
    {
        public Guid? DeniedSessionId { get; private set; }

        public Task DenySessionAsync(Guid sessionId, TimeSpan ttl, CancellationToken ct = default)
        {
            DeniedSessionId = sessionId;
            return Task.CompletedTask;
        }

        public Task<bool> IsSessionDeniedAsync(Guid sessionId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class ExplodingUserRepository : IUserRepository
    {
        public Task<IReadOnlyList<User>> GetActiveStaffForSlotGatingAsync(
            Guid tenantId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<User>>([]);

        public Task<IReadOnlyList<User>> GetActiveByRoleAsync(
            Guid tenantId,
            Guid roleId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<User>>([]);

        public Task<PagedResult<TenantOwnerReconciliationResponse>> ListTenantOwnersForReconciliationAsync(
            int page,
            int size,
            CancellationToken ct = default
        ) => Task.FromResult(new PagedResult<TenantOwnerReconciliationResponse>([], page, size, 0));

        public Task<PagedResult<CustomerPortalAccountReconciliationResponse>> ListPortalAccountsForReconciliationAsync(
            int page,
            int size,
            CancellationToken ct = default
        ) => Task.FromResult(new PagedResult<CustomerPortalAccountReconciliationResponse>([], page, size, 0));

        public Task<PagedResult<StaffUserReconciliationResponse>> ListStaffUsersForReconciliationAsync(
            int page,
            int size,
            CancellationToken ct = default
        ) => Task.FromResult(new PagedResult<StaffUserReconciliationResponse>([], page, size, 0));

        public Task<PagedResult<UserDirectoryReconciliationResponse>> ListUsersForDirectoryReconciliationAsync(
            int page,
            int size,
            CancellationToken ct = default
        ) => Task.FromResult(new PagedResult<UserDirectoryReconciliationResponse>([], page, size, 0));

        public Task<int> CountActiveAdminsAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<User?> GetByEmailAsync(
            Guid tenantId,
            string email,
            UserAccountKind kind,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<bool> EmailExistsAsync(
            Guid tenantId,
            string email,
            UserAccountKind kind,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<User?> GetPortalUserByCustomerAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<User?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> GetActiveTenantIdsByEmailAsync(
            string email,
            UserAccountKind kind,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task AddAsync(User user, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<User?> GetPrimaryAdminAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<(IReadOnlyList<User> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            int page,
            int size,
            string? search,
            bool? isActive,
            Guid? customerId = null,
            UserAccountKind? accountKind = null,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    private sealed class ExplodingTenantRegistry : ITenantRegistry
    {
        public Task<TaxVision.Auth.Domain.Tenants.Tenant?> GetByIdAsync(
            Guid tenantId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task UpsertCreatedAsync(
            Guid tenantId,
            string name,
            string subDomain,
            TenantKind kind,
            string defaultTimeZoneId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task SetActiveAsync(Guid tenantId, bool isActive, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SetBillingBlockedAsync(
            Guid tenantId,
            bool blocked,
            string? reason,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    private sealed class ExplodingRoleRepository : IRoleRepository
    {
        public Task<IReadOnlyList<UserPermissionDeny>> GetActiveDeniesAsync(
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<UserPermissionDeny>>([]);

        public Task<IReadOnlyList<(Guid UserId, Guid TenantId)>> GetUsersWithExpiredDeniesAsync(
            DateTime nowUtc,
            int take,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<(Guid UserId, Guid TenantId)>>([]);

        public Task<int> RemoveExpiredDeniesAsync(Guid userId, DateTime nowUtc, CancellationToken ct = default) =>
            Task.FromResult(0);

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Role>>> GetRolesByUsersAsync(
            IReadOnlyCollection<Guid> userIds,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<Role>>>(
                new Dictionary<Guid, IReadOnlyList<Role>>()
            );

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetDeniedPermissionIdsByUsersAsync(
            IReadOnlyCollection<Guid> userIds,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>>(
                new Dictionary<Guid, IReadOnlyList<Guid>>()
            );

        public Task<Role?> GetByIdAsync(Guid roleId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Role>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Role>> GetByIdsAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> roleIds,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task AddAsync(Role role, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> NameExistsAsync(Guid tenantId, string name, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> CountUsersInRoleAsync(Guid roleId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Permission>> GetPermissionsCatalogAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Role>> GetUserRolesAsync(Guid userId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetEffectivePermissionCodesAsync(
            Guid userId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task ReplaceUserRolesAsync(
            Guid userId,
            IReadOnlyCollection<Guid> roleIds,
            Guid? assignedByUserId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task EnsureSystemRolesAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task EnsureSystemRolesCommittedAsync(Guid tenantId, CancellationToken ct = default) =>
            EnsureSystemRolesAsync(tenantId, ct);

        public Task<IReadOnlyList<Guid>> GetDeniedPermissionIdsAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task ReplaceUserDeniesAsync(
            Guid userId,
            IReadOnlyCollection<PermissionDenyInput> denies,
            Guid? deniedByUserId,
            CancellationToken ct = default
        ) => Task.CompletedTask;

        public Task<Role?> GetSystemRoleAsync(Guid tenantId, string systemRoleName, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class ExplodingAuthSessionIssuer : IAuthSessionIssuer
    {
        public Task<IssuedTokens> StartSessionAsync(
            User user,
            string effectiveTimeZoneId,
            IReadOnlyCollection<string> roles,
            IReadOnlyCollection<string> authMethods,
            string? deviceName,
            SessionSurface surface,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IssuedTokens> JoinSessionAsync(
            UserSession session,
            User user,
            string effectiveTimeZoneId,
            IReadOnlyCollection<string> roles,
            IReadOnlyCollection<string> authMethods,
            SessionSurface surface,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IssuedTokens> RotateAsync(
            RefreshToken currentToken,
            UserSession session,
            User user,
            string effectiveTimeZoneId,
            IReadOnlyCollection<string> roles,
            IReadOnlyCollection<string> authMethods,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}
