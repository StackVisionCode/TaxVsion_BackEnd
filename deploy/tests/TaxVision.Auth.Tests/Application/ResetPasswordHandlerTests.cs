using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Credentials.Commands;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Mfa;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;
using Xunit;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Al resetear la contraseña, los demás enlaces pendientes de esa cuenta dejan de servir; los de otra
/// cuenta con el mismo email (otra oficina) siguen vigentes.</summary>
public sealed class ResetPasswordHandlerTests
{
    private const string NewPassword = "a-brand-new-passphrase";

    private readonly AccountSessionFixture _world = new();
    private readonly InMemoryCredentialTokenRepository _credentials = new();
    private readonly PredictableTokenService _tokens = new();
    private readonly RecordingSessionRevocationPublisher _revocations = new();
    private readonly InMemoryMfaRepository _mfa = new();

    [Fact]
    public async Task Resetting_revokes_the_other_pending_links_of_the_account()
    {
        var used = IssueLink(_world.Admin, out var rawUsed);
        var older = IssueLink(_world.Admin, out _);
        var otherOffice = User.Register(
            Guid.NewGuid(),
            "Ada",
            "Admin",
            _world.Admin.Email,
            "h",
            UserActorType.TenantAdmin
        ).Value;
        _world.Users.Add(otherOffice);
        var otherAccount = IssueLink(otherOffice, out _);

        var result = await ResetAsync(rawUsed);

        Assert.True(result.IsSuccess);
        Assert.NotNull(used.UsedAtUtc);
        Assert.NotNull(older.RevokedAtUtc);
        Assert.Equal(ResetRevocationReason.PasswordChanged, older.RevocationReason);
        Assert.True(otherAccount.IsUsable(DateTime.UtcNow));
    }

    [Fact]
    public async Task A_revoked_link_can_no_longer_reset_the_password()
    {
        IssueLink(_world.Admin, out var rawUsed);
        IssueLink(_world.Admin, out var rawOlder);
        await ResetAsync(rawUsed);

        var result = await ResetAsync(rawOlder);

        Assert.Equal("Auth.InvalidResetToken", result.Error.Code);
    }

    /// <summary>Un enlace de reset emitido para la oficina A no se puede canjear desde el subdominio de la
    /// oficina B — el UI ya rebota al load del validate, este guard es la defensa en profundidad.</summary>
    [Fact]
    public async Task Submitting_from_a_different_office_host_is_rejected()
    {
        IssueLink(_world.Admin, out var rawToken);
        var otherOffice = Tenant
            .Register(Guid.NewGuid(), "other", "Other", TenantKind.Customer, "America/New_York")
            .Value;
        _world.Tenants.Add(otherOffice);

        var result = await ResetAsync(rawToken, hostTenantId: otherOffice.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("Auth.InvalidResetToken", result.Error.Code);
    }

    /// <summary>El Host de un tenant Platform (api.*) o un Host no resuelto son entrada general: no acotan.</summary>
    [Fact]
    public async Task Submitting_without_a_host_hint_still_works()
    {
        IssueLink(_world.Admin, out var rawToken);

        var result = await ResetAsync(rawToken, hostTenantId: null);

        Assert.True(result.IsSuccess);
    }

    // Corte en tiempo real (auth:session-revoked) y, con D-05, también olvida los dispositivos de confianza.
    [Fact]
    public async Task Resetting_cuts_every_session_in_real_time_and_forgets_trusted_devices()
    {
        var (sessionId, _) = await _world.StartWorkspaceSessionAsync();
        var device = TrustedDevice.Create(
            _world.Admin.TenantId,
            _world.Admin.Id,
            "device-hash",
            "Chrome",
            TimeSpan.FromDays(30)
        );
        _mfa.Devices.Add(device);
        IssueLink(_world.Admin, out var rawToken);

        var result = await ResetAsync(rawToken);

        Assert.True(result.IsSuccess);
        Assert.Contains(sessionId, _world.Denylist.Denied);
        Assert.Equal((sessionId, "password_change"), Assert.Single(_revocations.Published));
        Assert.False(device.IsActive);
    }

    // F3-L6: la bitácora del canje dice de qué origen era el link.
    [Fact]
    public async Task Completing_an_office_link_records_its_origin()
    {
        var link = IssueLink(_world.Admin, out var rawToken);
        _credentials.PasswordResets.Remove(link);
        var office = PasswordResetToken.Create(
            link.TenantId,
            link.UserId,
            link.TokenHash,
            null,
            TimeSpan.FromHours(24),
            ResetOrigin.Administrative,
            Guid.NewGuid()
        );
        _credentials.PasswordResets.Add(office);

        var result = await ResetAsync(rawToken);

        Assert.True(result.IsSuccess);
        var log = Assert.Single(_world.Audit.Logs, l => l.Action == "auth.password.reset_completed" && l.Success);
        Assert.Contains("Administrative", log.DetailsJson);
    }

    // 03 §10: el reset tampoco acepta la contraseña actual (y cuenta como intento).
    [Fact]
    public async Task Resetting_to_the_current_password_is_rejected()
    {
        var link = IssueLink(_world.Admin, out var rawToken);

        var result = await ResetPasswordHandler.Handle(
            new ResetPasswordCommand(rawToken, "current-violet-harbor"),
            _credentials,
            _tokens,
            _world.Users,
            _world.Tenants,
            new SessionAccessCutoff(_world.Sessions, _world.Denylist, _revocations),
            _mfa,
            new SameAsCurrentHasher("current-violet-harbor"),
            _world.Audit,
            _world.Request,
            _world.Correlation,
            _world.UnitOfWork,
            new NoUserLoginLock(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.Equal("User.Password", result.Error.Code);
        Assert.Equal(1, link.Attempts);
    }

    private sealed class SameAsCurrentHasher(string current) : TaxVision.Auth.Application.Abstractions.IPasswordHasher
    {
        public string Hash(string password) => $"hashed:{password}";

        public bool Verify(string password, string hash) => password == current;
    }

    // I-3: una cuenta asegurada sale con su link y vuelve a Active.
    [Fact]
    public async Task Resetting_a_secured_account_with_its_link_clears_the_credential_state()
    {
        _world.Admin.MarkResetRequired(
            CredentialAdministrationReason.Create(CredentialAdministrationReasonCode.SuspectedCompromise, null).Value,
            Guid.NewGuid(),
            DateTime.UtcNow
        );
        IssueLink(_world.Admin, out var rawToken);

        var result = await ResetAsync(rawToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(CredentialStatus.Active, _world.Admin.CredentialStatus);
    }

    private PasswordResetToken IssueLink(User user, out string rawToken)
    {
        rawToken = _tokens.GenerateToken();
        var token = PasswordResetToken.Create(
            user.TenantId,
            user.Id,
            _tokens.Hash(rawToken),
            "127.0.0.1",
            TimeSpan.FromMinutes(30)
        );
        _credentials.PasswordResets.Add(token);
        return token;
    }

    private Task<Result> ResetAsync(string rawToken, Guid? hostTenantId = null) =>
        ResetPasswordHandler.Handle(
            new ResetPasswordCommand(rawToken, NewPassword, hostTenantId),
            _credentials,
            _tokens,
            _world.Users,
            _world.Tenants,
            new SessionAccessCutoff(_world.Sessions, _world.Denylist, _revocations),
            _mfa,
            new PrefixedPasswordHasher(),
            _world.Audit,
            _world.Request,
            _world.Correlation,
            _world.UnitOfWork,
            new NoUserLoginLock(),
            new FakeMessageBus(),
            CancellationToken.None
        );
}
