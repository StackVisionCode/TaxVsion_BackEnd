using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Sessions;
using TaxVision.Auth.Domain.Users;
using Xunit;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Fase 18.3 — RefreshToken host binding: un token emitido para un tenant no debe canjearse
/// desde el subdominio de otro.</summary>
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
        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<User?> GetByEmailAsync(Guid tenantId, string email, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> EmailExistsAsync(Guid tenantId, string email, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<User?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> GetActiveTenantIdsByEmailAsync(string email, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddAsync(User user, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<(IReadOnlyList<User> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            int page,
            int size,
            string? search,
            bool? isActive,
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
    }

    private sealed class ExplodingRoleRepository : IRoleRepository
    {
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
