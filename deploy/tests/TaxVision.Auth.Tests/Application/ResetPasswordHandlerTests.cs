using BuildingBlocks.Results;
using TaxVision.Auth.Application.Credentials.Commands;
using TaxVision.Auth.Domain.Credentials;
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

    private Task<Result> ResetAsync(string rawToken) =>
        ResetPasswordHandler.Handle(
            new ResetPasswordCommand(rawToken, NewPassword),
            _credentials,
            _tokens,
            _world.Users,
            _world.Sessions,
            _world.Denylist,
            new PrefixedPasswordHasher(),
            _world.Audit,
            _world.Request,
            _world.Correlation,
            _world.UnitOfWork,
            new FakeMessageBus(),
            CancellationToken.None
        );
}
