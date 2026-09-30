using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Credentials.Commands;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;
using Xunit;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Forgot password. Cubre la anti-enumeración (siempre éxito, el throttle corta antes de tocar la DB,
/// un email desconocido no publica nada), el alcance (subdominio de una oficina → solo esa oficina; entrada
/// general → una por oficina del email) y el ActorType de cada reset, que decide si el link va al portal o al CRM.</summary>
public sealed class ForgotPasswordHandlerTests
{
    [Fact]
    public async Task Throttled_returns_success_without_touching_the_user_repository()
    {
        var throttler = new FakeThrottler { RetryAfter = TimeSpan.FromSeconds(30) };
        var users = new ExplodingUserRepository();
        var bus = new FakeMessageBus();

        var result = await ForgotPasswordHandler.Handle(
            new ForgotPasswordCommand("someone@example.com"),
            users,
            new InMemoryTenantRegistry(),
            new CapturingCredentialTokenRepository(),
            new StubSecureTokenService(),
            throttler,
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.False(throttler.RequestRegistered);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Unknown_email_returns_success_and_publishes_nothing()
    {
        var users = new MultiOfficeUserRepository(); // sin oficinas sembradas
        var bus = new FakeMessageBus();
        var credentials = new CapturingCredentialTokenRepository();
        var throttler = new FakeThrottler { RetryAfter = null };

        var result = await ForgotPasswordHandler.Handle(
            new ForgotPasswordCommand("nobody@example.com"),
            users,
            new InMemoryTenantRegistry(),
            credentials,
            new StubSecureTokenService(),
            throttler,
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Empty(bus.Published);
        Assert.Empty(credentials.Added);
        Assert.True(throttler.RequestRegistered);
    }

    [Fact]
    public async Task Multi_office_email_emits_one_reset_per_office_with_its_actor_type()
    {
        var users = new MultiOfficeUserRepository();
        const string email = "ana@example.com";
        var clientTenant = users.AddOffice(email, UserActorType.CustomerPortal);
        var staffTenant = users.AddOffice(email, UserActorType.TenantEmployee);

        var bus = new FakeMessageBus();

        var result = await ForgotPasswordHandler.Handle(
            new ForgotPasswordCommand(email),
            users,
            new InMemoryTenantRegistry(),
            new CapturingCredentialTokenRepository(),
            new StubSecureTokenService(),
            new FakeThrottler { RetryAfter = null },
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var events = bus.Published.OfType<PasswordResetRequestedIntegrationEvent>().ToList();
        Assert.Equal(2, events.Count);

        var client = Assert.Single(events, e => e.TenantId == clientTenant);
        Assert.Equal(nameof(UserActorType.CustomerPortal), client.ActorType);

        var staff = Assert.Single(events, e => e.TenantId == staffTenant);
        Assert.Equal(nameof(UserActorType.TenantEmployee), staff.ActorType);
    }

    // ---- dobles ----

    [Fact]
    public async Task A_person_with_both_accounts_gets_one_reset_per_account_each_naming_its_office()
    {
        var world = DualAccounts();

        var bus = await ForgotAsync(world, accountKind: null);

        var resets = bus.Published.OfType<PasswordResetRequestedIntegrationEvent>().ToList();
        Assert.Equal(2, resets.Count);
        Assert.Contains(resets, reset => reset.ActorType == "CustomerPortal");
        Assert.Contains(resets, reset => reset.ActorType == "TenantEmployee");
        Assert.All(resets, reset => Assert.Equal(world.Tenant.Name, reset.TenantName));
    }

    [Fact]
    public async Task The_client_login_only_resets_the_portal_account()
    {
        var world = DualAccounts();

        var bus = await ForgotAsync(world, UserAccountKind.Portal);

        var reset = Assert.Single(bus.Published.OfType<PasswordResetRequestedIntegrationEvent>());
        Assert.Equal("CustomerPortal", reset.ActorType);
    }

    [Fact]
    public async Task From_an_office_subdomain_only_that_office_is_reset()
    {
        var offices = new TwoOffices();

        var bus = await offices.ForgotFromHostAsync(offices.First.Id);

        var reset = Assert.Single(bus.Published.OfType<PasswordResetRequestedIntegrationEvent>());
        Assert.Equal(offices.First.Id, reset.TenantId);
    }

    // api.* resuelve al tenant Platform: es la entrada general, no una oficina.
    [Fact]
    public async Task From_the_platform_host_every_office_is_reset()
    {
        var offices = new TwoOffices();

        var bus = await offices.ForgotFromHostAsync(offices.Platform.Id);

        var resets = bus.Published.OfType<PasswordResetRequestedIntegrationEvent>().ToList();
        Assert.Equal(
            new[] { offices.First.Id, offices.Second.Id }.Order(),
            resets.Select(reset => reset.TenantId).Order()
        );
    }

    [Fact]
    public async Task Without_a_host_office_every_office_is_reset()
    {
        var offices = new TwoOffices();

        var bus = await offices.ForgotFromHostAsync(hostTenantId: null);

        Assert.Equal(2, bus.Published.OfType<PasswordResetRequestedIntegrationEvent>().Count());
    }

    [Fact]
    public async Task From_an_office_where_the_email_has_no_account_nothing_is_sent()
    {
        var offices = new TwoOffices();

        var bus = await offices.ForgotFromHostAsync(offices.WithoutAccount.Id);

        Assert.Empty(bus.Published);
    }

    private static AccountSessionFixture DualAccounts()
    {
        var world = new AccountSessionFixture();
        world.Users.Add(
            User.Register(world.Tenant.Id, "Ana", "Staff", "ana@example.com", "h", UserActorType.TenantEmployee).Value
        );
        world.Users.Add(
            User.Register(
                world.Tenant.Id,
                "Ana",
                "Client",
                "ana@example.com",
                "h",
                UserActorType.CustomerPortal,
                Guid.NewGuid()
            ).Value
        );
        return world;
    }

    /// <summary>La misma persona es staff en dos oficinas de cliente; hay además una oficina sin su cuenta y el
    /// tenant Platform.</summary>
    private sealed class TwoOffices
    {
        private readonly InMemoryUserRepository _users = new();
        private readonly InMemoryTenantRegistry _tenants = new();

        public TwoOffices()
        {
            First = AddTenant("coretaxpro", TenantKind.Customer);
            Second = AddTenant("castillogarcia", TenantKind.Customer);
            WithoutAccount = AddTenant("other", TenantKind.Customer);
            Platform = AddTenant("api", TenantKind.Platform);
            _users.Add(
                User.Register(First.Id, "Ana", "Staff", "ana@example.com", "h", UserActorType.TenantEmployee).Value
            );
            _users.Add(
                User.Register(Second.Id, "Ana", "Owner", "ana@example.com", "h", UserActorType.TenantAdmin).Value
            );
        }

        public Tenant First { get; }
        public Tenant Second { get; }
        public Tenant WithoutAccount { get; }
        public Tenant Platform { get; }

        public async Task<FakeMessageBus> ForgotFromHostAsync(Guid? hostTenantId)
        {
            var bus = new FakeMessageBus();
            await ForgotPasswordHandler.Handle(
                new ForgotPasswordCommand("ana@example.com", UserAccountKind.Staff, hostTenantId),
                _users,
                _tenants,
                new CapturingCredentialTokenRepository(),
                new StubSecureTokenService(),
                new PermissiveLoginThrottler(),
                new FakeAuthAuditWriter(),
                new FakeRequestContext(),
                new FakeCorrelationContext(),
                new FakeUnitOfWork(),
                bus,
                CancellationToken.None
            );
            return bus;
        }

        private Tenant AddTenant(string subDomain, TenantKind kind)
        {
            var tenant = Tenant.Register(Guid.NewGuid(), subDomain, subDomain, kind, "America/New_York").Value;
            _tenants.Add(tenant);
            return tenant;
        }
    }

    private static async Task<FakeMessageBus> ForgotAsync(AccountSessionFixture world, UserAccountKind? accountKind)
    {
        var bus = new FakeMessageBus();
        await ForgotPasswordHandler.Handle(
            new ForgotPasswordCommand("ana@example.com", accountKind),
            world.Users,
            world.Tenants,
            new CapturingCredentialTokenRepository(),
            new StubSecureTokenService(),
            new PermissiveLoginThrottler(),
            world.Audit,
            world.Request,
            world.Correlation,
            world.UnitOfWork,
            bus,
            CancellationToken.None
        );
        return bus;
    }

    private sealed class FakeThrottler : ILoginThrottler
    {
        public TimeSpan? RetryAfter { get; set; }
        public bool RequestRegistered { get; private set; }

        public Task<TimeSpan?> GetIpRetryAfterAsync(string? ipAddress, CancellationToken ct = default) =>
            Task.FromResult<TimeSpan?>(null);

        public Task RegisterFailureAsync(string? ipAddress, CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> IsOtpResendThrottledAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task RegisterOtpSentAsync(Guid userId, CancellationToken ct = default) => Task.CompletedTask;

        public Task<TimeSpan?> GetPasswordResetRetryAfterAsync(
            string email,
            string? ipAddress,
            CancellationToken ct = default
        ) => Task.FromResult(RetryAfter);

        public Task RegisterPasswordResetRequestAsync(string email, string? ipAddress, CancellationToken ct = default)
        {
            RequestRegistered = true;
            return Task.CompletedTask;
        }

        public Task<TimeSpan?> GetInvitationAcceptRetryAfterAsync(string? ipAddress, CancellationToken ct = default) =>
            Task.FromResult<TimeSpan?>(null);

        public Task RegisterInvitationAcceptAttemptAsync(string? ipAddress, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<Result> AuthorizeOnboardingChallengeCreationAsync(
            string email,
            string ipAddress,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success());

        public Task<Result> AuthorizeOnboardingResendAsync(Guid challengeId, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());
    }

    private sealed class StubSecureTokenService : ISecureTokenService
    {
        private int _n;

        public string GenerateToken(int byteLength = 32) => $"raw-token-{_n++}";

        public string GenerateNumericCode(int digits = 6) => "123456";

        public string Hash(string rawToken) => $"hash:{rawToken}";
    }

    private sealed class CapturingCredentialTokenRepository : ICredentialTokenRepository
    {
        public List<PasswordResetToken> Added { get; } = [];

        public Task AddPasswordResetAsync(PasswordResetToken token, CancellationToken ct = default)
        {
            Added.Add(token);
            return Task.CompletedTask;
        }

        public Task<PasswordResetToken?> GetPasswordResetByHashAsync(
            string tokenHash,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<PasswordResetToken>> GetPendingPasswordResetsAsync(
            Guid userId,
            DateTime utcNow,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task AddEmailVerificationAsync(EmailVerificationToken token, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<EmailVerificationToken?> GetEmailVerificationByHashAsync(
            string tokenHash,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task AddPhoneVerificationAsync(PhoneVerificationToken token, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PhoneVerificationToken?> GetActivePhoneVerificationAsync(
            Guid userId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    private sealed class MultiOfficeUserRepository : IUserRepository
    {
        // email → (tenantId → user)
        private readonly Dictionary<string, Dictionary<Guid, User>> _byEmail = new(StringComparer.OrdinalIgnoreCase);

        public Guid AddOffice(string email, UserActorType actorType)
        {
            var tenantId = Guid.NewGuid();
            Guid? customerId = actorType == UserActorType.CustomerPortal ? Guid.NewGuid() : null;
            var user = User.Register(tenantId, "Test", "User", email, "hash", actorType, customerId).Value;
            if (!_byEmail.TryGetValue(email, out var offices))
                _byEmail[email] = offices = [];
            offices[tenantId] = user;
            return tenantId;
        }

        public Task<IReadOnlyList<Guid>> GetActiveTenantIdsByEmailAsync(
            string email,
            UserAccountKind kind,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<Guid>>(
                _byEmail.TryGetValue(email, out var offices)
                    ? offices.Where(office => office.Value.AccountKind == kind).Select(office => office.Key).ToList()
                    : []
            );

        public Task<User?> GetByEmailAsync(
            Guid tenantId,
            string email,
            UserAccountKind kind,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                _byEmail.TryGetValue(email, out var offices)
                && offices.GetValueOrDefault(tenantId) is { } user
                && user.AccountKind == kind
                    ? user
                    : null
            );

        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();

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

    private sealed class ExplodingUserRepository : IUserRepository
    {
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
}
