using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Credentials.Commands;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Users;
using Xunit;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Forgot password CENTRAL (desde app.*, sin oficina en el Host). Cubre la seguridad del
/// flujo: anti-enumeración (siempre éxito, el throttle corta antes de tocar la DB, un email
/// desconocido no publica nada) y el descubrimiento cross-tenant (un email en N oficinas emite un
/// reset por cada una, con el ActorType de esa oficina para que el link vaya al portal o al CRM).</summary>
public sealed class ForgotPasswordCentralHandlerTests
{
    [Fact]
    public async Task Throttled_returns_success_without_touching_the_user_repository()
    {
        var throttler = new FakeThrottler { RetryAfter = TimeSpan.FromSeconds(30) };
        var users = new ExplodingUserRepository();
        var bus = new FakeMessageBus();

        var result = await ForgotPasswordCentralHandler.Handle(
            new ForgotPasswordCentralCommand("someone@example.com"),
            users,
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

        var result = await ForgotPasswordCentralHandler.Handle(
            new ForgotPasswordCentralCommand("nobody@example.com"),
            users,
            credentials,
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
        Assert.Empty(bus.Published);
        Assert.Empty(credentials.Added);
    }

    [Fact]
    public async Task Multi_office_email_emits_one_reset_per_office_with_its_actor_type()
    {
        var users = new MultiOfficeUserRepository();
        const string email = "ana@example.com";
        var clientTenant = users.AddOffice(email, UserActorType.CustomerPortal);
        var staffTenant = users.AddOffice(email, UserActorType.TenantEmployee);

        var bus = new FakeMessageBus();

        var result = await ForgotPasswordCentralHandler.Handle(
            new ForgotPasswordCentralCommand(email),
            users,
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

        public Task<IReadOnlyList<Guid>> GetActiveTenantIdsByEmailAsync(string email, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>(
                _byEmail.TryGetValue(email, out var offices) ? offices.Keys.ToList() : []
            );

        public Task<User?> GetByEmailAsync(Guid tenantId, string email, CancellationToken ct = default) =>
            Task.FromResult(_byEmail.TryGetValue(email, out var offices) ? offices.GetValueOrDefault(tenantId) : null);

        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();

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
}
