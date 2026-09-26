using BuildingBlocks.Results;
using BuildingBlocks.Security;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Mfa;
using TaxVision.Auth.Domain.RefreshTokens;

namespace TaxVision.Auth.Tests.Application;

public sealed class ReauthenticateHandlerTests
{
    private const string Password = "secret";
    private const string TotpCode = "654321";

    [Fact]
    public async Task The_right_password_issues_an_elevated_token_for_the_same_session_and_surface()
    {
        var world = new AccountSessionFixture();
        var (sessionId, _) = await world.StartWorkspaceSessionAsync();

        var result = await HandleAsync(world, sessionId, Password, surface: SessionSurface.Account);

        Assert.True(result.IsSuccess);
        var (issuedSid, surface, reauthAt, methods) = world.Jwt.Issued[^1];
        Assert.Equal(sessionId, issuedSid);
        Assert.Equal(SessionSurface.Account, surface);
        Assert.Equal(result.Value.ReauthenticatedAtUtc, reauthAt);
        Assert.Equal(new[] { "pwd" }, methods);
        Assert.Contains(world.Audit.Logs, log => log.Action == AuthAuditAction.Reauthenticated && log.Success);
    }

    [Fact]
    public async Task A_wrong_password_fails_without_ending_the_session_and_counts_toward_lockout()
    {
        var world = new AccountSessionFixture();
        var (sessionId, _) = await world.StartWorkspaceSessionAsync();

        var result = await HandleAsync(world, sessionId, "nope");

        Assert.Equal("Auth.ReauthenticationFailed", result.Error.Code);
        Assert.Equal(1, world.Admin.FailedLoginCount);
        Assert.True((await world.Sessions.GetSessionByIdAsync(sessionId))!.IsActive);
        Assert.Contains(world.Audit.Logs, log => log.Action == AuthAuditAction.ReauthenticationFailed && !log.Success);
    }

    [Fact]
    public async Task Repeated_failures_lock_the_account_and_report_the_wait()
    {
        var world = new AccountSessionFixture();
        var (sessionId, _) = await world.StartWorkspaceSessionAsync();
        for (var attempt = 0; attempt < LockoutPolicy.MaxFailedAttempts; attempt++)
            await HandleAsync(world, sessionId, "nope");

        var result = await HandleAsync(world, sessionId, Password);

        Assert.Equal("Auth.LockedOut", result.Error.Code);
        Assert.True(result.Error.RetryAfterSeconds > 0);
    }

    [Fact]
    public async Task With_an_authenticator_app_the_code_is_asked_for_before_counting_anything()
    {
        var world = new AccountSessionFixture();
        var (sessionId, _) = await world.StartWorkspaceSessionAsync();
        var mfa = WithTotp(world);

        var result = await HandleAsync(world, sessionId, Password, mfa: mfa);

        Assert.Equal("Auth.MfaCodeRequired", result.Error.Code);
        Assert.Equal(0, world.Admin.FailedLoginCount);
    }

    [Fact]
    public async Task With_an_authenticator_app_a_wrong_code_fails_and_the_right_one_elevates()
    {
        var world = new AccountSessionFixture();
        var (sessionId, _) = await world.StartWorkspaceSessionAsync();
        var mfa = WithTotp(world);

        var wrong = await HandleAsync(world, sessionId, Password, "000000", mfa);
        var right = await HandleAsync(world, sessionId, Password, TotpCode, mfa);

        Assert.Equal("Auth.ReauthenticationFailed", wrong.Error.Code);
        Assert.True(right.IsSuccess);
        Assert.Equal(new[] { "pwd", "totp" }, world.Jwt.Issued[^1].AuthMethods);
    }

    [Fact]
    public async Task A_closed_session_cannot_be_elevated()
    {
        var world = new AccountSessionFixture();
        var (sessionId, _) = await world.StartWorkspaceSessionAsync();
        await world.Sessions.RevokeSessionAsync(sessionId, "user_logout");

        var result = await HandleAsync(world, sessionId, Password);

        Assert.Equal("Auth.SessionRevoked", result.Error.Code);
    }

    private static FakeMfaRepository WithTotp(AccountSessionFixture world)
    {
        var method = MfaMethod.Create(world.Tenant.Id, world.Admin.Id, MfaMethodType.Totp, "totp-secret", null).Value;
        method.Confirm();
        return new FakeMfaRepository([method]);
    }

    private static Task<Result<ReauthenticateResponse>> HandleAsync(
        AccountSessionFixture world,
        Guid sessionId,
        string password,
        string? code = null,
        FakeMfaRepository? mfa = null,
        SessionSurface surface = SessionSurface.Workspace
    ) =>
        ReauthenticateHandler.Handle(
            new ReauthenticateCommand(world.Admin.Id, sessionId, surface, password, code),
            world.Users,
            world.Tenants,
            world.Sessions,
            world.Roles,
            new PrefixPasswordHasher(),
            mfa ?? new FakeMfaRepository([]),
            new FixedTotpService(),
            new PlainSecretProtector(),
            world.TokenService,
            world.Jwt,
            world.Audit,
            world.Request,
            world.Correlation,
            world.UnitOfWork,
            new FakeMessageBus(),
            CancellationToken.None
        );

    /// <summary>El fixture guarda "hashed:secret" como hash de la contraseña "secret".</summary>
    private sealed class PrefixPasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => $"hashed:{password}";

        public bool Verify(string password, string hash) => hash == Hash(password);
    }

    private sealed class FixedTotpService : ITotpService
    {
        public string GenerateSecret() => throw new NotSupportedException();

        public string BuildOtpAuthUri(string accountName, string base32Secret, string issuer) =>
            throw new NotSupportedException();

        public bool ValidateCode(string base32Secret, string code, DateTime utcNow) => code == TotpCode;
    }

    private sealed class PlainSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;

        public bool TryUnprotect(string? protectedValue, out string plaintext, out SecretUnprotectFailure failure)
        {
            plaintext = protectedValue ?? string.Empty;
            failure = SecretUnprotectFailure.None;
            return protectedValue is not null;
        }
    }

    private sealed class FakeMfaRepository(IReadOnlyList<MfaMethod> methods) : IMfaRepository
    {
        public Task<IReadOnlyList<MfaMethod>> GetMethodsAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(methods);

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

        public Task<TenantMfaPolicy?> GetPolicyAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddPolicyAsync(TenantMfaPolicy policy, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
