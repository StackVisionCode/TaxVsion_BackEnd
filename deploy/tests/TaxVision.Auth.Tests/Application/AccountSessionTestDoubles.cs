using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Invitations;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Sessions;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Sesiones y refresh tokens en memoria con la misma semántica de revocación que el repositorio real.</summary>
internal sealed class InMemorySessionRepository : ISessionRepository
{
    public List<UserSession> Sessions { get; } = [];
    public List<RefreshToken> Tokens { get; } = [];

    public Task AddSessionAsync(UserSession session, CancellationToken ct = default)
    {
        Sessions.Add(session);
        return Task.CompletedTask;
    }

    public Task<UserSession?> GetSessionByIdAsync(Guid sessionId, CancellationToken ct = default) =>
        Task.FromResult(Sessions.FirstOrDefault(session => session.Id == sessionId));

    public Task<IReadOnlyList<UserSession>> GetActiveSessionsByUserAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<UserSession>>(
            Sessions.Where(session => session.UserId == userId && session.IsActive).ToList()
        );

    public Task AddTokenAsync(RefreshToken token, CancellationToken ct = default)
    {
        Tokens.Add(token);
        return Task.CompletedTask;
    }

    public Task<RefreshToken?> GetTokenByHashAsync(string tokenHash, CancellationToken ct = default) =>
        Task.FromResult(Tokens.FirstOrDefault(token => token.TokenHash == tokenHash));

    public Task<int> RevokeSessionAsync(Guid sessionId, string reason, CancellationToken ct = default)
    {
        Sessions.FirstOrDefault(session => session.Id == sessionId)?.Revoke(reason);
        return Task.FromResult(RevokeTokens(token => token.SessionId == sessionId, reason));
    }

    public Task<int> RevokeSurfaceTokensAsync(
        Guid sessionId,
        SessionSurface surface,
        string reason,
        CancellationToken ct = default
    ) => Task.FromResult(RevokeTokens(token => token.SessionId == sessionId && token.Surface == surface, reason));

    public Task<bool> HasActiveChainAsync(Guid sessionId, SessionSurface surface, CancellationToken ct = default) =>
        Task.FromResult(
            Tokens.Any(token => token.SessionId == sessionId && token.Surface == surface && token.IsActive)
        );

    public Task<int> RevokeAllForUserAsync(
        Guid userId,
        string reason,
        Guid? exceptSessionId = null,
        CancellationToken ct = default
    )
    {
        var sessions = Sessions
            .Where(session => session.UserId == userId && session.IsActive && session.Id != exceptSessionId)
            .ToList();
        foreach (var session in sessions)
            session.Revoke(reason);
        RevokeTokens(token => token.UserId == userId && token.SessionId != exceptSessionId, reason);
        return Task.FromResult(sessions.Count);
    }

    public Task<int> RevokeAllForTenantAsync(Guid tenantId, string reason, CancellationToken ct = default) =>
        throw new NotSupportedException();

    private int RevokeTokens(Func<RefreshToken, bool> match, string reason)
    {
        var active = Tokens.Where(token => token.RevokedAtUtc is null && match(token)).ToList();
        foreach (var token in active)
            token.Revoke(reason);
        return active.Count;
    }
}

internal sealed class InMemoryUserRepository : IUserRepository
{
    private readonly Dictionary<Guid, User> _users = [];

    public void Add(User user) => _users[user.Id] = user;

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_users.GetValueOrDefault(id));

    // Misma semántica que el repositorio real: el email es único por oficina dentro de cada tipo de cuenta.
    public Task<User?> GetByEmailAsync(
        Guid tenantId,
        string email,
        UserAccountKind kind,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            _users.Values.FirstOrDefault(user =>
                user.TenantId == tenantId && user.Email == email && user.AccountKind == kind
            )
        );

    public Task<bool> EmailExistsAsync(
        Guid tenantId,
        string email,
        UserAccountKind kind,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            _users.Values.Any(user => user.TenantId == tenantId && user.Email == email && user.AccountKind == kind)
        );

    public Task<User?> GetPortalUserByCustomerAsync(Guid tenantId, Guid customerId, CancellationToken ct = default) =>
        Task.FromResult(
            _users.Values.FirstOrDefault(user =>
                user.TenantId == tenantId
                && user.CustomerId == customerId
                && user.ActorType == UserActorType.CustomerPortal
            )
        );

    public Task<User?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<Guid>> GetActiveTenantIdsByEmailAsync(
        string email,
        UserAccountKind kind,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<Guid>>(
            _users
                .Values.Where(user => user.Email == email && user.IsActive && user.AccountKind == kind)
                .Select(user => user.TenantId)
                .Distinct()
                .ToList()
        );

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

internal sealed class InMemoryTenantRegistry : ITenantRegistry
{
    private readonly Dictionary<Guid, Tenant> _tenants = [];

    public void Add(Tenant tenant) => _tenants[tenant.Id] = tenant;

    public Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(_tenants.GetValueOrDefault(tenantId));

    public Task UpsertCreatedAsync(
        Guid tenantId,
        string name,
        string subDomain,
        BuildingBlocks.Tenancy.TenantKind kind,
        string defaultTimeZoneId,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task SetActiveAsync(Guid tenantId, bool isActive, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task SetBillingBlockedAsync(Guid tenantId, bool blocked, string? reason, CancellationToken ct = default) =>
        throw new NotSupportedException();
}

/// <summary>Usuario sin roles asignados: alcanza para resolver los claims de la sesión.</summary>
internal sealed class NoRolesRepository : IRoleRepository
{
    public Task<IReadOnlyList<Role>> GetUserRolesAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Role>>([]);

    public Task<IReadOnlyList<string>> GetEffectivePermissionCodesAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task<IReadOnlyList<Guid>> GetDeniedPermissionIdsAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Guid>>([]);

    public Task<Role?> GetByIdAsync(Guid roleId, CancellationToken ct = default) => throw new NotSupportedException();

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

    public Task ReplaceUserDeniesAsync(
        Guid userId,
        IReadOnlyCollection<Guid> permissionIds,
        Guid? deniedByUserId,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task EnsureSystemRolesAsync(Guid tenantId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task EnsureSystemRolesCommittedAsync(Guid tenantId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<Role?> GetSystemRoleAsync(Guid tenantId, string systemRoleName, CancellationToken ct = default) =>
        throw new NotSupportedException();
}

/// <summary>Tokens opacos predecibles: el hash es el raw con prefijo, así el test puede canjearlos.</summary>
internal sealed class PredictableTokenService : ISecureTokenService
{
    private int _next;

    public string GenerateToken(int byteLength = 32) => $"raw-{++_next}";

    public string GenerateNumericCode(int digits = 6) => "123456";

    public string Hash(string rawToken) => $"hash:{rawToken}";
}

/// <summary>Registra superficie y step-up de cada access token emitido.</summary>
internal sealed class CapturingJwtTokenGenerator : IJwtTokenGenerator
{
    public List<(
        Guid SessionId,
        SessionSurface Surface,
        DateTime? ReauthenticatedAtUtc,
        string[] AuthMethods
    )> Issued { get; } = [];

    public AccessToken Generate(
        User user,
        string effectiveTimeZoneId,
        Guid sessionId,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> authMethods,
        SessionSurface surface = SessionSurface.Workspace,
        DateTime? reauthenticatedAtUtc = null
    )
    {
        Issued.Add((sessionId, surface, reauthenticatedAtUtc, [.. authMethods]));
        return new AccessToken($"access-{surface}-{Issued.Count}", 900);
    }

    public AccessToken GenerateServiceToken(
        Guid tenantId,
        string clientId,
        IReadOnlyCollection<string> permissions,
        int lifetimeMinutes
    ) => throw new NotSupportedException();

    public AccessToken GenerateTenantRegistrationTicket(string slug, string email, DateTime expiresAtUtc) =>
        throw new NotSupportedException();
}

internal sealed class InMemoryAccountHandoffTicketStore : IAccountHandoffTicketStore
{
    private readonly Dictionary<Guid, AccountHandoffPayload> _tickets = [];

    public TimeSpan Lifetime => TimeSpan.FromSeconds(60);

    public Task<Guid> IssueAsync(AccountHandoffPayload payload, CancellationToken ct = default)
    {
        var ticket = Guid.NewGuid();
        _tickets[ticket] = payload;
        return Task.FromResult(ticket);
    }

    public Task<AccountHandoffPayload?> ConsumeAsync(Guid ticket, CancellationToken ct = default) =>
        Task.FromResult(_tickets.Remove(ticket, out var payload) ? payload : null);
}

internal sealed class RecordingAccessTokenDenylist : IAccessTokenDenylist
{
    public List<Guid> Denied { get; } = [];

    public Task DenySessionAsync(Guid sessionId, TimeSpan ttl, CancellationToken ct = default)
    {
        Denied.Add(sessionId);
        return Task.CompletedTask;
    }

    public Task<bool> IsSessionDeniedAsync(Guid sessionId, CancellationToken ct = default) =>
        Task.FromResult(Denied.Contains(sessionId));
}

/// <summary>
/// Oficina con su TenantAdmin y el cableado real de emisión (<see cref="Infrastructure.Security.AuthSessionIssuer"/>)
/// sobre repositorios en memoria, para ejercitar cadenas por superficie de punta a punta.
/// </summary>
internal sealed class AccountSessionFixture
{
    public Tenant Tenant { get; }
    public User Admin { get; }
    public InMemorySessionRepository Sessions { get; } = new();
    public InMemoryUserRepository Users { get; } = new();
    public InMemoryTenantRegistry Tenants { get; } = new();
    public NoRolesRepository Roles { get; } = new();
    public PredictableTokenService TokenService { get; } = new();
    public CapturingJwtTokenGenerator Jwt { get; } = new();
    public InMemoryAccountHandoffTicketStore HandoffTickets { get; } = new();
    public RecordingAccessTokenDenylist Denylist { get; } = new();
    public FakeAuthAuditWriter Audit { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public FakeRequestContext Request { get; } = new();
    public FakeCorrelationContext Correlation { get; } = new();
    public IAuthSessionIssuer Issuer { get; }

    public AccountSessionFixture(UserActorType actorType = UserActorType.TenantAdmin)
    {
        var tenantId = Guid.NewGuid();
        Tenant = Tenant
            .Register(tenantId, "office", "Office", BuildingBlocks.Tenancy.TenantKind.Customer, "America/New_York")
            .Value;
        Admin = User.Register(tenantId, "Ada", "Admin", "ada@example.com", "hashed:secret", actorType).Value;
        Tenants.Add(Tenant);
        Users.Add(Admin);
        Issuer = new TaxVision.Auth.Infrastructure.Security.AuthSessionIssuer(
            Sessions,
            TokenService,
            Jwt,
            Request,
            Microsoft.Extensions.Options.Options.Create(
                new TaxVision.Auth.Infrastructure.Security.RefreshTokenOptions()
            )
        );
    }

    /// <summary>Sesión abierta en el CRM: devuelve el sid y el refresh crudo de la cadena del workspace.</summary>
    public async Task<(Guid SessionId, string RefreshToken)> StartWorkspaceSessionAsync()
    {
        var issued = await Issuer.StartSessionAsync(
            Admin,
            "America/New_York",
            [],
            ["pwd"],
            "Chrome",
            SessionSurface.Workspace
        );
        return (issued.SessionId, issued.RefreshToken);
    }
}

internal sealed class InMemoryInvitationRepository : IInvitationRepository
{
    public List<Invitation> Invitations { get; } = [];

    public Task<Invitation?> GetByIdAsync(Guid invitationId, CancellationToken ct = default) =>
        Task.FromResult(Invitations.FirstOrDefault(invitation => invitation.Id == invitationId));

    public Task<Invitation?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        Task.FromResult(Invitations.FirstOrDefault(invitation => invitation.TokenHash == tokenHash));

    public Task<bool> HasPendingAsync(
        Guid tenantId,
        string email,
        UserAccountKind kind,
        CancellationToken ct = default
    ) => Task.FromResult(Pending(tenantId, email, kind).Any());

    public Task<Invitation?> GetPendingAsync(
        Guid tenantId,
        string email,
        UserAccountKind kind,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            Pending(tenantId, email, kind).OrderByDescending(invitation => invitation.CreatedAtUtc).FirstOrDefault()
        );

    public Task AddAsync(Invitation invitation, CancellationToken ct = default)
    {
        Invitations.Add(invitation);
        return Task.CompletedTask;
    }

    public Task<int> CountPendingAsync(Guid tenantId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<(IReadOnlyList<Invitation> Items, int TotalCount)> GetPagedAsync(
        Guid tenantId,
        InvitationStatus? status,
        int page,
        int size,
        Guid? customerId = null,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    private IEnumerable<Invitation> Pending(Guid tenantId, string email, UserAccountKind kind) =>
        Invitations.Where(invitation =>
            invitation.TenantId == tenantId
            && invitation.Email == email
            && invitation.AccountKind == kind
            && invitation.Status == InvitationStatus.Pending
            && invitation.ExpiresAtUtc > DateTime.UtcNow
        );
}

/// <summary>Tokens de invitación predecibles para poder comprobar que un reenvío regenera el enlace.</summary>
internal sealed class SequentialInvitationTokenService : IInvitationTokenService
{
    private int _next;

    public InvitationToken Generate()
    {
        var raw = $"invite-{++_next}";
        return new InvitationToken(raw, Hash(raw));
    }

    public string Hash(string rawToken) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)));
}

/// <summary>Nunca frena: para tests que no ejercen el throttle.</summary>
internal sealed class PermissiveLoginThrottler : ILoginThrottler
{
    public Task<TimeSpan?> GetIpRetryAfterAsync(string? ipAddress, CancellationToken ct = default) =>
        Task.FromResult<TimeSpan?>(null);

    public Task RegisterFailureAsync(string? ipAddress, CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> IsOtpResendThrottledAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(false);

    public Task RegisterOtpSentAsync(Guid userId, CancellationToken ct = default) => Task.CompletedTask;

    public Task<TimeSpan?> GetPasswordResetRetryAfterAsync(
        string email,
        string? ipAddress,
        CancellationToken ct = default
    ) => Task.FromResult<TimeSpan?>(null);

    public Task RegisterPasswordResetRequestAsync(string email, string? ipAddress, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<TimeSpan?> GetInvitationAcceptRetryAfterAsync(string? ipAddress, CancellationToken ct = default) =>
        Task.FromResult<TimeSpan?>(null);

    public Task RegisterInvitationAcceptAttemptAsync(string? ipAddress, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<BuildingBlocks.Results.Result> AuthorizeOnboardingChallengeCreationAsync(
        string email,
        string ipAddress,
        CancellationToken ct = default
    ) => Task.FromResult(BuildingBlocks.Results.Result.Success());

    public Task<BuildingBlocks.Results.Result> AuthorizeOnboardingResendAsync(
        Guid challengeId,
        CancellationToken ct = default
    ) => Task.FromResult(BuildingBlocks.Results.Result.Success());
}

/// <summary>Usuarios sin MFA y oficina sin política: el login emite directo.</summary>
internal sealed class NoMfaRepository : IMfaRepository
{
    public Task<IReadOnlyList<TaxVision.Auth.Domain.Mfa.MfaMethod>> GetMethodsAsync(
        Guid userId,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<TaxVision.Auth.Domain.Mfa.MfaMethod>>([]);

    public Task<TaxVision.Auth.Domain.Mfa.TenantMfaPolicy?> GetPolicyAsync(
        Guid tenantId,
        CancellationToken ct = default
    ) => Task.FromResult<TaxVision.Auth.Domain.Mfa.TenantMfaPolicy?>(null);

    public Task<IReadOnlyList<TaxVision.Auth.Domain.Mfa.RecoveryCode>> GetRecoveryCodesAsync(
        Guid userId,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<TaxVision.Auth.Domain.Mfa.RecoveryCode>>([]);

    public Task<TaxVision.Auth.Domain.Mfa.MfaMethod?> GetMethodAsync(
        Guid userId,
        TaxVision.Auth.Domain.Mfa.MfaMethodType type,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<TaxVision.Auth.Domain.Mfa.MfaMethod?> GetMethodByIdAsync(
        Guid methodId,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task AddMethodAsync(TaxVision.Auth.Domain.Mfa.MfaMethod method, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public void RemoveMethod(TaxVision.Auth.Domain.Mfa.MfaMethod method) => throw new NotSupportedException();

    public Task AddChallengeAsync(TaxVision.Auth.Domain.Mfa.MfaChallenge challenge, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<TaxVision.Auth.Domain.Mfa.MfaChallenge?> GetChallengeByTicketHashAsync(
        string ticketHash,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task AddRecoveryCodesAsync(
        IEnumerable<TaxVision.Auth.Domain.Mfa.RecoveryCode> codes,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public void RemoveRecoveryCodes(IEnumerable<TaxVision.Auth.Domain.Mfa.RecoveryCode> codes) =>
        throw new NotSupportedException();

    public Task<TaxVision.Auth.Domain.Mfa.TrustedDevice?> GetTrustedDeviceByHashAsync(
        string deviceTokenHash,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<IReadOnlyList<TaxVision.Auth.Domain.Mfa.TrustedDevice>> GetTrustedDevicesAsync(
        Guid userId,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task AddTrustedDeviceAsync(TaxVision.Auth.Domain.Mfa.TrustedDevice device, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task AddPolicyAsync(TaxVision.Auth.Domain.Mfa.TenantMfaPolicy policy, CancellationToken ct = default) =>
        throw new NotSupportedException();
}

/// <summary>El hash de una contraseña es la contraseña con prefijo: cada cuenta del test tiene la suya.</summary>
/// <summary>Enlaces de reset en memoria con la misma semántica que el repositorio real.</summary>
internal sealed class InMemoryCredentialTokenRepository : ICredentialTokenRepository
{
    public List<PasswordResetToken> PasswordResets { get; } = [];

    public Task AddPasswordResetAsync(PasswordResetToken token, CancellationToken ct = default)
    {
        PasswordResets.Add(token);
        return Task.CompletedTask;
    }

    public Task<PasswordResetToken?> GetPasswordResetByHashAsync(string tokenHash, CancellationToken ct = default) =>
        Task.FromResult(PasswordResets.FirstOrDefault(token => token.TokenHash == tokenHash));

    public Task<IReadOnlyList<PasswordResetToken>> GetPendingPasswordResetsAsync(
        Guid userId,
        DateTime utcNow,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<PasswordResetToken>>(
            PasswordResets
                .Where(token =>
                    token.UserId == userId
                    && token.UsedAtUtc == null
                    && token.RevokedAtUtc == null
                    && token.ExpiresAtUtc > utcNow
                )
                .ToList()
        );

    public Task AddEmailVerificationAsync(EmailVerificationToken token, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<EmailVerificationToken?> GetEmailVerificationByHashAsync(
        string tokenHash,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task AddPhoneVerificationAsync(PhoneVerificationToken token, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<PhoneVerificationToken?> GetActivePhoneVerificationAsync(Guid userId, CancellationToken ct = default) =>
        throw new NotSupportedException();
}

internal sealed class PrefixedPasswordHasher : IPasswordHasher
{
    public string Hash(string password) => $"hashed:{password}";

    public bool Verify(string password, string hash) => hash == Hash(password);
}

internal sealed class InMemoryDiscoverySessionStore : IDiscoverySessionStore
{
    public Dictionary<Guid, DiscoverySession> Sessions { get; } = [];

    public Task<Guid> StoreAsync(DiscoverySession session, CancellationToken ct = default)
    {
        var reference = Guid.NewGuid();
        Sessions[reference] = session;
        return Task.FromResult(reference);
    }

    public Task<DiscoverySession?> PeekAsync(Guid reference, CancellationToken ct = default) =>
        Task.FromResult(Sessions.GetValueOrDefault(reference));

    public Task ConsumeAsync(Guid reference, CancellationToken ct = default)
    {
        Sessions.Remove(reference);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryHandoffTicketStore : IHandoffTicketStore
{
    public Dictionary<Guid, HandoffTicketPayload> Tickets { get; } = [];

    public Task<Guid> IssueAsync(HandoffTicketPayload payload, CancellationToken ct = default)
    {
        var ticket = Guid.NewGuid();
        Tickets[ticket] = payload;
        return Task.FromResult(ticket);
    }

    public Task<HandoffTicketPayload?> ConsumeAsync(Guid ticket, CancellationToken ct = default) =>
        Task.FromResult(Tickets.Remove(ticket, out var payload) ? payload : null);
}
