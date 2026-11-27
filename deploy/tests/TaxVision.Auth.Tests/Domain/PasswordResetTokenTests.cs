using TaxVision.Auth.Domain.Credentials;
using Xunit;

namespace TaxVision.Auth.Tests.Domain;

public sealed class PasswordResetTokenTests
{
    private static PasswordResetToken NewToken(
        ResetOrigin origin = ResetOrigin.SelfService,
        Guid? requestedByUserId = null
    ) =>
        PasswordResetToken.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new string('a', 64),
            "127.0.0.1",
            TimeSpan.FromMinutes(30),
            origin,
            requestedByUserId
        );

    [Fact]
    public void A_revoked_link_can_no_longer_be_used()
    {
        var token = NewToken();
        var now = DateTime.UtcNow;

        token.Revoke(now, ResetRevocationReason.Superseded);

        Assert.False(token.IsUsable(now));
        Assert.Equal(now, token.RevokedAtUtc);
    }

    // I-5: un token usado no se revoca ni se cancela.
    [Fact]
    public void Revoking_a_used_link_keeps_it_as_used()
    {
        var token = NewToken();
        token.MarkUsed();

        token.Revoke(DateTime.UtcNow, ResetRevocationReason.CancelledByOffice, Guid.NewGuid());

        Assert.NotNull(token.UsedAtUtc);
        Assert.Null(token.RevokedAtUtc);
        Assert.Null(token.RevocationReason);
        Assert.Null(token.RevokedByUserId);
        Assert.Equal(PasswordResetTokenStatus.Completed, token.StatusAt(DateTime.UtcNow));
    }

    [Fact]
    public void Revoke_records_the_reason_and_who_revoked_it_once()
    {
        var token = NewToken();
        var owner = Guid.NewGuid();
        var now = DateTime.UtcNow;

        token.Revoke(now, ResetRevocationReason.CancelledByOffice, owner);
        token.Revoke(now.AddMinutes(1), ResetRevocationReason.PasswordChanged);

        Assert.Equal(ResetRevocationReason.CancelledByOffice, token.RevocationReason);
        Assert.Equal(owner, token.RevokedByUserId);
        Assert.Equal(now, token.RevokedAtUtc);
    }

    [Fact]
    public void A_link_keeps_its_origin_and_requester()
    {
        var owner = Guid.NewGuid();

        var token = NewToken(ResetOrigin.Administrative, owner);

        Assert.Equal(ResetOrigin.Administrative, token.Origin);
        Assert.Equal(owner, token.RequestedByUserId);
    }

    [Theory]
    [InlineData(ResetRevocationReason.Superseded, PasswordResetTokenStatus.Superseded)]
    [InlineData(ResetRevocationReason.CancelledByOffice, PasswordResetTokenStatus.Cancelled)]
    [InlineData(ResetRevocationReason.PasswordChanged, PasswordResetTokenStatus.PasswordChanged)]
    [InlineData(ResetRevocationReason.AccountSecured, PasswordResetTokenStatus.AccountSecured)]
    public void A_revoked_link_reports_why(ResetRevocationReason reason, PasswordResetTokenStatus expected)
    {
        var token = NewToken();

        token.Revoke(DateTime.UtcNow, reason);

        Assert.Equal(expected, token.StatusAt(DateTime.UtcNow));
    }

    [Fact]
    public void A_link_is_pending_until_it_expires_or_runs_out_of_attempts()
    {
        var token = NewToken();
        var now = DateTime.UtcNow;

        Assert.Equal(PasswordResetTokenStatus.Pending, token.StatusAt(now));
        Assert.Equal(PasswordResetTokenStatus.Expired, token.StatusAt(token.ExpiresAtUtc));

        for (var i = 0; i < PasswordResetToken.MaxAttempts; i++)
            token.RegisterAttempt();
        Assert.Equal(PasswordResetTokenStatus.Exhausted, token.StatusAt(now));
    }
}
