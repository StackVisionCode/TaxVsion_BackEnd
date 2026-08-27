using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Sessions.Commands;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Sessions;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;
using Xunit;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// Sesión única (takeover con vale). Blinda el invariante central: si el usuario ya tiene una sesión
/// activa, el login NO mintea — devuelve un vale; y el canje del vale revoca las anteriores, emite la
/// nueva y avisa en tiempo real a cada dispositivo revocado.
/// </summary>
public sealed class SessionTakeoverTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static User BuildUser() =>
        User.Register(TenantId, "Test", "User", "user@example.com", "hash", UserActorType.TenantAdmin).Value;

    private static Tenant BuildTenant() =>
        Tenant.Register(TenantId, "office", "Office", TenantKind.Customer, "America/Santo_Domingo").Value;

    private static UserSession BuildSession(Guid userId) =>
        UserSession.Start(TenantId, userId, "Device", "ua", "127.0.0.1");

    [Fact]
    public async Task Existing_session_returns_takeover_ticket_and_does_not_mint()
    {
        var user = BuildUser();
        var sessions = new FakeSessions { Active = [BuildSession(user.Id)] };
        var store = new FakeTakeoverStore();
        var issuer = new FakeIssuer();

        var outcome = await SessionEstablishment.IssueOrRequireTakeoverAsync(
            user,
            BuildTenant(),
            ["pwd"],
            deviceName: null,
            mustEnrollMfa: false,
            new FakeRoles(),
            issuer,
            sessions,
            store,
            CancellationToken.None
        );

        Assert.True(outcome.TakeoverRequired);
        Assert.NotNull(outcome.TakeoverTicket);
        Assert.Equal(1, store.IssueCount);
        Assert.Equal(0, issuer.StartCount); // NO se mintea con sesión previa
    }

    [Fact]
    public async Task No_existing_session_issues_tokens()
    {
        var user = BuildUser();
        var sessions = new FakeSessions { Active = [] };
        var store = new FakeTakeoverStore();
        var issuer = new FakeIssuer();

        var outcome = await SessionEstablishment.IssueOrRequireTakeoverAsync(
            user,
            BuildTenant(),
            ["pwd"],
            deviceName: null,
            mustEnrollMfa: false,
            new FakeRoles(),
            issuer,
            sessions,
            store,
            CancellationToken.None
        );

        Assert.False(outcome.TakeoverRequired);
        Assert.NotNull(outcome.Tokens);
        Assert.Equal(0, store.IssueCount);
        Assert.Equal(1, issuer.StartCount);
    }

    [Fact]
    public async Task Takeover_invalid_ticket_returns_TakeoverInvalid()
    {
        var store = new FakeTakeoverStore { ToConsume = null };

        var result = await TakeoverSessionHandler.Handle(
            new TakeoverSessionCommand(Guid.NewGuid()),
            store,
            new ExplodingUsers(),
            new ExplodingTenants(),
            new FakeRoles(),
            new FakeIssuer(),
            new FakeSessions(),
            new RecordingDenylist(),
            new RecordingRevocationPublisher(),
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Auth.TakeoverInvalid", result.Error.Code);
    }

    [Fact]
    public async Task Takeover_valid_ticket_revokes_previous_issues_and_publishes()
    {
        var user = BuildUser();
        var previous = new List<UserSession> { BuildSession(user.Id), BuildSession(user.Id) };
        var sessions = new FakeSessions { Active = previous };
        var denylist = new RecordingDenylist();
        var publisher = new RecordingRevocationPublisher();
        var issuer = new FakeIssuer();
        var store = new FakeTakeoverStore
        {
            ToConsume = new SessionTakeoverPayload(TenantId, user.Id, ["pwd"], null, MustEnrollMfa: false),
        };

        var result = await TakeoverSessionHandler.Handle(
            new TakeoverSessionCommand(Guid.NewGuid()),
            store,
            new StubUsers(user),
            new StubTenants(BuildTenant()),
            new FakeRoles(),
            issuer,
            sessions,
            denylist,
            publisher,
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Tokens);
        Assert.True(sessions.RevokeAllCalled);
        Assert.Equal(1, issuer.StartCount);
        Assert.Equal(2, denylist.Denied.Count); // ambas sesiones previas
        Assert.Equal(2, publisher.Published.Count); // aviso en tiempo real a ambas
    }

    // ---- dobles ----

    private sealed class FakeSessions : ISessionRepository
    {
        public List<UserSession> Active { get; set; } = [];
        public bool RevokeAllCalled { get; private set; }

        public Task<IReadOnlyList<UserSession>> GetActiveSessionsByUserAsync(
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<UserSession>>(Active);

        public Task<int> RevokeAllForUserAsync(
            Guid userId,
            string reason,
            Guid? exceptSessionId = null,
            CancellationToken ct = default
        )
        {
            RevokeAllCalled = true;
            return Task.FromResult(Active.Count);
        }

        public Task AddSessionAsync(UserSession session, CancellationToken ct = default) => Task.CompletedTask;

        public Task AddTokenAsync(
            TaxVision.Auth.Domain.RefreshTokens.RefreshToken token,
            CancellationToken ct = default
        ) => Task.CompletedTask;

        public Task<UserSession?> GetSessionByIdAsync(Guid sessionId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TaxVision.Auth.Domain.RefreshTokens.RefreshToken?> GetTokenByHashAsync(
            string tokenHash,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<int> RevokeSessionAsync(Guid sessionId, string reason, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> RevokeAllForTenantAsync(Guid tenantId, string reason, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeIssuer : IAuthSessionIssuer
    {
        public int StartCount { get; private set; }

        public Task<IssuedTokens> StartSessionAsync(
            User user,
            string effectiveTimeZoneId,
            IReadOnlyCollection<string> roles,
            IReadOnlyCollection<string> authMethods,
            string? deviceName,
            CancellationToken ct = default
        )
        {
            StartCount++;
            return Task.FromResult(new IssuedTokens("access", "refresh", 900, Guid.NewGuid()));
        }

        public Task<IssuedTokens> RotateAsync(
            TaxVision.Auth.Domain.RefreshTokens.RefreshToken currentToken,
            UserSession session,
            User user,
            string effectiveTimeZoneId,
            IReadOnlyCollection<string> roles,
            IReadOnlyCollection<string> authMethods,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    private sealed class FakeTakeoverStore : ISessionTakeoverTicketStore
    {
        public int IssueCount { get; private set; }
        public SessionTakeoverPayload? ToConsume { get; set; }

        public Task<Guid> IssueAsync(SessionTakeoverPayload payload, CancellationToken ct = default)
        {
            IssueCount++;
            return Task.FromResult(Guid.NewGuid());
        }

        public Task<SessionTakeoverPayload?> ConsumeAsync(Guid ticket, CancellationToken ct = default) =>
            Task.FromResult(ToConsume);
    }

    private sealed class FakeRoles : IRoleRepository
    {
        public Task<IReadOnlyList<Role>> GetUserRolesAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Role>>([]);

        public Task<IReadOnlyList<string>> GetEffectivePermissionCodesAsync(
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<string>>([]);

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

    private sealed class RecordingDenylist : IAccessTokenDenylist
    {
        public List<Guid> Denied { get; } = [];

        public Task DenySessionAsync(Guid sessionId, TimeSpan ttl, CancellationToken ct = default)
        {
            Denied.Add(sessionId);
            return Task.CompletedTask;
        }

        public Task<bool> IsSessionDeniedAsync(Guid sessionId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingRevocationPublisher : ISessionRevocationPublisher
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

    private sealed class StubUsers(User user) : IUserRepository
    {
        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<User?>(user);

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

    private sealed class ExplodingUsers : IUserRepository
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

    private sealed class StubTenants(Tenant tenant) : ITenantRegistry
    {
        public Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<Tenant?>(tenant);

        public Task UpsertCreatedAsync(
            Guid tenantId,
            string subdomain,
            string name,
            TenantKind kind,
            string timeZoneId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task SetActiveAsync(Guid tenantId, bool isActive, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class ExplodingTenants : ITenantRegistry
    {
        public Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task UpsertCreatedAsync(
            Guid tenantId,
            string subdomain,
            string name,
            TenantKind kind,
            string timeZoneId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task SetActiveAsync(Guid tenantId, bool isActive, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
