using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Credentials.Commands;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Users;
using Xunit;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Fase 18.1 — throttle de ForgotPassword corta antes de tocar el repo de usuarios.</summary>
public sealed class ForgotPasswordHandlerTests
{
    [Fact]
    public async Task Throttled_returns_success_without_touching_the_user_repository()
    {
        var throttler = new FakeLoginThrottler { RetryAfter = TimeSpan.FromSeconds(30) };
        var users = new ExplodingUserRepository();
        var credentials = new ExplodingCredentialTokenRepository();
        var bus = new FakeMessageBus();

        var result = await ForgotPasswordHandler.Handle(
            new ForgotPasswordCommand(Guid.NewGuid(), "someone@example.com"),
            users,
            credentials,
            new FakeSecureTokenService(),
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
    public async Task Registers_the_request_when_not_throttled()
    {
        var throttler = new FakeLoginThrottler { RetryAfter = null };
        var users = new NullUserRepository();
        var credentials = new ExplodingCredentialTokenRepository();

        var result = await ForgotPasswordHandler.Handle(
            new ForgotPasswordCommand(Guid.NewGuid(), "someone@example.com"),
            users,
            credentials,
            new FakeSecureTokenService(),
            throttler,
            new FakeAuthAuditWriter(),
            new FakeRequestContext(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.True(throttler.RequestRegistered);
    }

    private sealed class FakeLoginThrottler : ILoginThrottler
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
    }

    private sealed class FakeSecureTokenService : ISecureTokenService
    {
        public string GenerateToken(int byteLength = 32) => "raw-token";

        public string GenerateNumericCode(int digits = 6) => "123456";

        public string Hash(string rawToken) => $"hash:{rawToken}";
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

    private sealed class NullUserRepository : IUserRepository
    {
        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<User?>(null);

        public Task<User?> GetByEmailAsync(Guid tenantId, string email, CancellationToken ct = default) =>
            Task.FromResult<User?>(null);

        public Task<bool> EmailExistsAsync(Guid tenantId, string email, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<User?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            Task.FromResult<User?>(null);

        public Task<IReadOnlyList<Guid>> GetActiveTenantIdsByEmailAsync(string email, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

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

    private sealed class ExplodingCredentialTokenRepository : ICredentialTokenRepository
    {
        public Task AddPasswordResetAsync(PasswordResetToken token, CancellationToken ct = default) =>
            throw new NotSupportedException();

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
}
