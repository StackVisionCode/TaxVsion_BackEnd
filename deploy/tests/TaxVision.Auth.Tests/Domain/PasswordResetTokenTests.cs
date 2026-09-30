using TaxVision.Auth.Domain.Credentials;
using Xunit;

namespace TaxVision.Auth.Tests.Domain;

public sealed class PasswordResetTokenTests
{
    private static PasswordResetToken NewToken() =>
        PasswordResetToken.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new string('a', 64),
            "127.0.0.1",
            TimeSpan.FromMinutes(30)
        );

    [Fact]
    public void A_revoked_link_can_no_longer_be_used()
    {
        var token = NewToken();
        var now = DateTime.UtcNow;

        token.Revoke(now);

        Assert.False(token.IsUsable(now));
        Assert.Equal(now, token.RevokedAtUtc);
    }

    [Fact]
    public void Revoking_a_used_link_keeps_it_as_used()
    {
        var token = NewToken();
        token.MarkUsed();

        token.Revoke(DateTime.UtcNow);

        Assert.NotNull(token.UsedAtUtc);
        Assert.Null(token.RevokedAtUtc);
    }
}
