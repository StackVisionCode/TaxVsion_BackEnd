using BuildingBlocks.Security;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.CentralLogin.Commands;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.Mfa;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Sessions;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;
using Xunit;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// Login central multi-tenant: discover (password contra cada oficina) → handoff (selector/MFA) →
/// from-ticket (canje en el subdominio). Reusa los fakes compartidos de TenantDomainTestDoubles.
/// </summary>
public sealed class CentralLoginHandlersTests
{
    private const string GoodPassword = "correct-horse";

    // --- discover-login ---

    [Fact]
    public async Task Discover_single_office_without_mfa_issues_the_ticket_directly()
    {
        var world = new World();
        var tenantId = world.AddOffice("acme", UserActorType.TenantEmployee);

        var result = await Discover(world, "user@example.com", GoodPassword);

        Assert.True(result.IsSuccess);
        Assert.Equal("acme", result.Value.Subdomain);
        Assert.NotNull(result.Value.Ticket);
        Assert.Null(result.Value.DiscoverySessionRef);
        Assert.Equal(new HandoffTicketPayload(tenantId, world.UserIn(tenantId)), world.Tickets.LastPayload);
    }

    [Fact]
    public async Task Discover_two_offices_returns_the_selector_without_a_ticket()
    {
        var world = new World();
        world.AddOffice("acme", UserActorType.TenantEmployee);
        world.AddOffice("globex", UserActorType.TenantEmployee);

        var result = await Discover(world, "user@example.com", GoodPassword);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Ticket);
        Assert.NotNull(result.Value.DiscoverySessionRef);
        Assert.Equal(2, result.Value.Offices!.Count);
        Assert.Null(world.Tickets.LastPayload);
    }

    [Fact]
    public async Task Discover_single_office_with_confirmed_mfa_returns_the_selector_to_ask_the_code()
    {
        // TenantAdmin CON método TOTP confirmado → hay que pedir código → selector, no vale directo.
        var world = new World();
        world.AddOffice("acme", UserActorType.TenantAdmin, enrollTotp: true);

        var result = await Discover(world, "user@example.com", GoodPassword);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Ticket);
        Assert.NotNull(result.Value.DiscoverySessionRef);
        Assert.True(Assert.Single(result.Value.Offices!).MfaRequired);
    }

    [Fact]
    public async Task Discover_admin_without_enrolled_mfa_goes_direct_and_flags_setup_on_exchange()
    {
        // MFA exigido por política pero sin método: se entra (no se bloquea) con flag de enrolar,
        // igual que el login directo. El vale viaja directo y from-ticket devuelve MfaSetupRequired.
        var world = new World();
        world.AddOffice("acme", UserActorType.TenantAdmin);

        var discover = await Discover(world, "user@example.com", GoodPassword);
        Assert.True(discover.IsSuccess);
        Assert.NotNull(discover.Value.Ticket);

        var session = await FromTicket(world, discover.Value.Ticket!.Value);
        Assert.True(session.IsSuccess);
        Assert.True(session.Value.MfaSetupRequired);
    }

    [Fact]
    public async Task Discover_without_matches_fails_generic_and_registers_the_ip()
    {
        var world = new World();
        world.AddOffice("acme", UserActorType.TenantEmployee);

        var result = await Discover(world, "user@example.com", "wrong-password");

        Assert.True(result.IsFailure);
        Assert.Equal("Auth.Invalid", result.Error.Code);
        Assert.Equal(1, world.Throttler.Failures);
    }

    [Fact]
    public async Task Discover_wrong_password_in_one_office_does_not_lock_that_account()
    {
        var world = new World();
        var tenantId = world.AddOffice("acme", UserActorType.TenantEmployee);

        await Discover(world, "user@example.com", "wrong-password");

        // El fallo de password NO descuenta el lockout por-cuenta: sólo cuenta el throttle por IP.
        Assert.Equal(0, world.Users.Get(tenantId).FailedLoginCount);
    }

    [Fact]
    public async Task Discover_throttled_ip_is_rejected_before_authenticating()
    {
        var world = new World();
        world.AddOffice("acme", UserActorType.TenantEmployee);
        world.Throttler.RetryAfter = TimeSpan.FromMinutes(1);

        var result = await Discover(world, "user@example.com", GoodPassword);

        Assert.True(result.IsFailure);
        Assert.Equal("Auth.LockedOut", result.Error.Code);
    }

    // --- handoff ---

    [Fact]
    public async Task Handoff_valid_choice_issues_the_ticket_and_consumes_the_session()
    {
        var world = new World();
        var a = world.AddOffice("acme", UserActorType.TenantEmployee);
        world.AddOffice("globex", UserActorType.TenantEmployee);
        var discover = await Discover(world, "user@example.com", GoodPassword);
        var sessionRef = discover.Value.DiscoverySessionRef!.Value;

        var result = await Handoff(world, sessionRef, a);

        Assert.True(result.IsSuccess);
        Assert.Equal("acme", result.Value.Subdomain);
        Assert.NotEqual(Guid.Empty, result.Value.Ticket);
        Assert.False(world.Sessions.Contains(sessionRef));
    }

    [Fact]
    public async Task Handoff_tenant_outside_the_authenticated_set_is_rejected()
    {
        var world = new World();
        world.AddOffice("acme", UserActorType.TenantEmployee);
        world.AddOffice("globex", UserActorType.TenantEmployee);
        var discover = await Discover(world, "user@example.com", GoodPassword);
        var sessionRef = discover.Value.DiscoverySessionRef!.Value;

        var result = await Handoff(world, sessionRef, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("Auth.HandoffInvalid", result.Error.Code);
    }

    [Fact]
    public async Task Handoff_expired_session_is_rejected()
    {
        var world = new World();
        var result = await Handoff(world, Guid.NewGuid(), Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("Auth.HandoffInvalid", result.Error.Code);
    }

    [Fact]
    public async Task Handoff_mfa_office_without_code_is_rejected_and_keeps_the_session()
    {
        var world = new World();
        // Admin CON método → el handoff exige código; sin él, falla y conserva la sesión.
        var admin = world.AddOffice("acme", UserActorType.TenantAdmin, enrollTotp: true);
        var discover = await Discover(world, "user@example.com", GoodPassword);
        var sessionRef = discover.Value.DiscoverySessionRef!.Value;

        var result = await Handoff(world, sessionRef, admin, mfaCode: null);

        Assert.True(result.IsFailure);
        Assert.Equal("Auth.MfaInvalid", result.Error.Code);
        // Peek, no consume: el usuario puede reintentar con el código dentro de la ventana.
        Assert.True(world.Sessions.Contains(sessionRef));
    }

    [Fact]
    public async Task Handoff_mfa_office_with_valid_code_issues_the_ticket()
    {
        var world = new World();
        var admin = world.AddOffice("acme", UserActorType.TenantAdmin, enrollTotp: true);
        var discover = await Discover(world, "user@example.com", GoodPassword);
        var sessionRef = discover.Value.DiscoverySessionRef!.Value;

        var result = await Handoff(world, sessionRef, admin, mfaCode: "valid-totp");

        Assert.True(result.IsSuccess);
        Assert.Equal("acme", result.Value.Subdomain);
        Assert.False(world.Sessions.Contains(sessionRef));
    }

    // --- from-ticket ---

    [Fact]
    public async Task FromTicket_valid_ticket_returns_session_tokens()
    {
        var world = new World();
        var tenantId = world.AddOffice("acme", UserActorType.TenantEmployee);
        var ticket = await world.Tickets.IssueAsync(new HandoffTicketPayload(tenantId, world.UserIn(tenantId)));

        var result = await FromTicket(world, ticket);

        Assert.True(result.IsSuccess);
        Assert.Equal("access", result.Value.AccessToken);
        Assert.Equal("refresh", result.Value.RefreshToken);
        Assert.False(result.Value.MfaSetupRequired);
    }

    [Fact]
    public async Task FromTicket_consumed_or_unknown_ticket_is_rejected()
    {
        var world = new World();
        world.AddOffice("acme", UserActorType.TenantEmployee);

        var result = await FromTicket(world, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("Auth.HandoffInvalid", result.Error.Code);
    }

    [Fact]
    public async Task FromTicket_same_ticket_twice_second_is_rejected()
    {
        // Anti-replay: el vale es de un solo uso (GETDEL). El segundo canje no encuentra el payload.
        var world = new World();
        var tenantId = world.AddOffice("acme", UserActorType.TenantEmployee);
        var ticket = await world.Tickets.IssueAsync(new HandoffTicketPayload(tenantId, world.UserIn(tenantId)));

        var first = await FromTicket(world, ticket);
        var second = await FromTicket(world, ticket);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsFailure);
        Assert.Equal("Auth.HandoffInvalid", second.Error.Code);
    }

    // --- drivers ---

    private static Task<BuildingBlocks.Results.Result<DiscoverLoginResponse>> Discover(
        World world,
        string email,
        string password
    ) =>
        DiscoverLoginHandler.Handle(
            new DiscoverLoginCommand(email, password),
            world.Users,
            world.Tenants,
            world.Hasher,
            world.Mfa,
            world.Sessions,
            world.Tickets,
            world.Throttler,
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            Microsoft.Extensions.Options.Options.Create(new TaxVision.Auth.Application.Common.MfaOptions()),
            CancellationToken.None
        );

    private static Task<BuildingBlocks.Results.Result<HandoffTicketView>> Handoff(
        World world,
        Guid sessionRef,
        Guid chosenTenantId,
        string? mfaCode = null
    ) =>
        IssueHandoffTicketHandler.Handle(
            new IssueHandoffTicketCommand(sessionRef, chosenTenantId, mfaCode),
            world.Sessions,
            world.Tickets,
            world.Tenants,
            world.Mfa,
            world.Totp,
            world.Protector,
            world.SecureTokens,
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

    private static Task<BuildingBlocks.Results.Result<HandoffSessionResponse>> FromTicket(World world, Guid ticket) =>
        ExchangeHandoffTicketHandler.Handle(
            new ExchangeHandoffTicketCommand(ticket),
            world.Tickets,
            world.Users,
            world.Tenants,
            world.Roles,
            world.Issuer,
            new EmptyUserSessionRepository(),
            new NoopSessionTakeoverTicketStore(),
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

    /// <summary>Estado compartido de un escenario: oficinas (tenant + user) sembradas y los dobles.</summary>
    private sealed class World
    {
        public FakeUserRepository Users { get; } = new();
        public FakeTenantRegistry Tenants { get; } = new();
        public FakePasswordHasher Hasher { get; } = new(GoodPassword);
        public FakeMfaRepository Mfa { get; } = new();
        public FakeDiscoverySessionStore Sessions { get; } = new();
        public FakeHandoffTicketStore Tickets { get; } = new();
        public FakeLoginThrottler Throttler { get; } = new();
        public FakeSecureTokenService SecureTokens { get; } = new();
        public FakeTotpService Totp { get; } = new();
        public FakeSecretProtector Protector { get; } = new();
        public FakeRoleRepository Roles { get; } = new();
        public FakeAuthSessionIssuer Issuer { get; } = new();

        private readonly Dictionary<Guid, Guid> _userByTenant = [];

        public Guid AddOffice(
            string subdomain,
            UserActorType actorType,
            string email = "user@example.com",
            bool enrollTotp = false
        )
        {
            var tenantId = Guid.NewGuid();
            var tenant = Tenant
                .Register(tenantId, subdomain, subdomain, TenantKind.Customer, "America/Santo_Domingo")
                .Value;
            var user = User.Register(tenantId, "Test", "User", email, "hash", actorType).Value;
            Tenants.Add(tenant);
            Users.Add(email, user);
            _userByTenant[tenantId] = user.Id;
            if (enrollTotp)
            {
                var method = MfaMethod.Create(tenantId, user.Id, MfaMethodType.Totp, "cipher", null).Value;
                method.Confirm();
                Mfa.Enroll(user.Id, method);
            }
            return tenantId;
        }

        public Guid UserIn(Guid tenantId) => _userByTenant[tenantId];
    }

    private sealed class FakePasswordHasher(string good) : IPasswordHasher
    {
        public string Hash(string password) => "hash";

        public bool Verify(string password, string hash) => password == good;
    }

    private sealed class FakeLoginThrottler : ILoginThrottler
    {
        public TimeSpan? RetryAfter { get; set; }
        public int Failures { get; private set; }

        public Task<TimeSpan?> GetIpRetryAfterAsync(string? ipAddress, CancellationToken ct = default) =>
            Task.FromResult(RetryAfter);

        public Task RegisterFailureAsync(string? ipAddress, CancellationToken ct = default)
        {
            Failures++;
            return Task.CompletedTask;
        }

        public Task<bool> IsOtpResendThrottledAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task RegisterOtpSentAsync(Guid userId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TimeSpan?> GetPasswordResetRetryAfterAsync(
            string email,
            string? ipAddress,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task RegisterPasswordResetRequestAsync(
            string email,
            string? ipAddress,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<TimeSpan?> GetInvitationAcceptRetryAfterAsync(string? ipAddress, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task RegisterInvitationAcceptAttemptAsync(string? ipAddress, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<BuildingBlocks.Results.Result> AuthorizeOnboardingChallengeCreationAsync(
            string email,
            string ipAddress,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<BuildingBlocks.Results.Result> AuthorizeOnboardingResendAsync(
            Guid challengeId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    private sealed class FakeDiscoverySessionStore : IDiscoverySessionStore
    {
        private readonly Dictionary<Guid, DiscoverySession> _store = [];

        public Task<Guid> StoreAsync(DiscoverySession session, CancellationToken ct = default)
        {
            var reference = Guid.NewGuid();
            _store[reference] = session;
            return Task.FromResult(reference);
        }

        public Task<DiscoverySession?> PeekAsync(Guid reference, CancellationToken ct = default) =>
            Task.FromResult(_store.GetValueOrDefault(reference));

        public Task ConsumeAsync(Guid reference, CancellationToken ct = default)
        {
            _store.Remove(reference);
            return Task.CompletedTask;
        }

        public bool Contains(Guid reference) => _store.ContainsKey(reference);
    }

    private sealed class FakeHandoffTicketStore : IHandoffTicketStore
    {
        private readonly Dictionary<Guid, HandoffTicketPayload> _store = [];

        public HandoffTicketPayload? LastPayload { get; private set; }

        public Task<Guid> IssueAsync(HandoffTicketPayload payload, CancellationToken ct = default)
        {
            var ticket = Guid.NewGuid();
            _store[ticket] = payload;
            LastPayload = payload;
            return Task.FromResult(ticket);
        }

        public Task<HandoffTicketPayload?> ConsumeAsync(Guid ticket, CancellationToken ct = default)
        {
            if (_store.Remove(ticket, out var payload))
                return Task.FromResult<HandoffTicketPayload?>(payload);
            return Task.FromResult<HandoffTicketPayload?>(null);
        }
    }

    private sealed class FakeSecureTokenService : ISecureTokenService
    {
        public string GenerateToken(int byteLength = 32) => "token";

        public string GenerateNumericCode(int digits = 6) => "123456";

        public string Hash(string rawToken) => rawToken;
    }

    private sealed class FakeTotpService : ITotpService
    {
        public string GenerateSecret() => "secret";

        public string BuildOtpAuthUri(string accountName, string base32Secret, string issuer) => "uri";

        public bool ValidateCode(string base32Secret, string code, DateTime utcNow) => code == "valid-totp";
    }

    private sealed class FakeSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;

        public bool TryUnprotect(string? protectedValue, out string plaintext, out SecretUnprotectFailure failure)
        {
            plaintext = protectedValue ?? string.Empty;
            failure = SecretUnprotectFailure.None;
            return protectedValue is not null;
        }
    }

    private sealed class FakeAuthSessionIssuer : IAuthSessionIssuer
    {
        public Task<IssuedTokens> StartSessionAsync(
            User user,
            string effectiveTimeZoneId,
            IReadOnlyCollection<string> roles,
            IReadOnlyCollection<string> authMethods,
            string? deviceName,
            CancellationToken ct = default
        ) => Task.FromResult(new IssuedTokens("access", "refresh", 900, Guid.NewGuid()));

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

    private sealed class FakeUserRepository : IUserRepository
    {
        // email → (tenantId → user)
        private readonly Dictionary<string, Dictionary<Guid, User>> _byEmail = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Guid, User> _byId = [];

        public void Add(string email, User user)
        {
            if (!_byEmail.TryGetValue(email, out var offices))
                _byEmail[email] = offices = [];
            offices[user.TenantId] = user;
            _byId[user.Id] = user;
        }

        public User Get(Guid tenantId) => _byId.Values.First(u => u.TenantId == tenantId);

        public Task<IReadOnlyList<Guid>> GetActiveTenantIdsByEmailAsync(string email, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>(
                _byEmail.TryGetValue(email, out var offices) ? offices.Keys.ToList() : []
            );

        public Task<User?> GetByEmailAsync(Guid tenantId, string email, CancellationToken ct = default) =>
            Task.FromResult(_byEmail.TryGetValue(email, out var offices) ? offices.GetValueOrDefault(tenantId) : null);

        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_byId.GetValueOrDefault(id));

        public Task<bool> EmailExistsAsync(Guid tenantId, string email, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<User?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
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

    private sealed class FakeTenantRegistry : ITenantRegistry
    {
        private readonly Dictionary<Guid, Tenant> _tenants = [];

        public void Add(Tenant tenant) => _tenants[tenant.Id] = tenant;

        public Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(_tenants.GetValueOrDefault(tenantId));

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

    private sealed class FakeMfaRepository : IMfaRepository
    {
        private readonly Dictionary<Guid, List<MfaMethod>> _methods = [];

        public void Enroll(Guid userId, MfaMethod method)
        {
            if (!_methods.TryGetValue(userId, out var list))
                _methods[userId] = list = [];
            list.Add(method);
        }

        public Task<TenantMfaPolicy?> GetPolicyAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<TenantMfaPolicy?>(null);

        public Task<IReadOnlyList<MfaMethod>> GetMethodsAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MfaMethod>>(_methods.TryGetValue(userId, out var list) ? list : []);

        public Task<IReadOnlyList<RecoveryCode>> GetRecoveryCodesAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RecoveryCode>>([]);

        public Task<MfaMethod?> GetMethodAsync(Guid userId, MfaMethodType type, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<MfaMethod?> GetMethodByIdAsync(Guid methodId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddMethodAsync(MfaMethod method, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void RemoveMethod(MfaMethod method) => throw new NotSupportedException();

        public Task AddChallengeAsync(MfaChallenge challenge, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<MfaChallenge?> GetChallengeByTicketHashAsync(string ticketHash, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddRecoveryCodesAsync(IEnumerable<RecoveryCode> codes, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void RemoveRecoveryCodes(IEnumerable<RecoveryCode> codes) => throw new NotSupportedException();

        public Task<TrustedDevice?> GetTrustedDeviceByHashAsync(
            string deviceTokenHash,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<TrustedDevice>> GetTrustedDevicesAsync(Guid userId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddTrustedDeviceAsync(TrustedDevice device, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddPolicyAsync(TenantMfaPolicy policy, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeRoleRepository : IRoleRepository
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
}
