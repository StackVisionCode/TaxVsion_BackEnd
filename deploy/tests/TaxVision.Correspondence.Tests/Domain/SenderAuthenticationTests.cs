using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Tests.Domain;

public sealed class SenderAuthenticationTests
{
    [Theory]
    [InlineData(EmailAuthResult.Fail, EmailAuthResult.Pass, EmailAuthResult.Pass)] // DMARC fail solo
    [InlineData(EmailAuthResult.Pass, EmailAuthResult.Fail, EmailAuthResult.Fail)] // SPF+DKIM fallan
    public void Unverified_when_authentication_fails(EmailAuthResult dmarc, EmailAuthResult spf, EmailAuthResult dkim)
    {
        Assert.Equal(SenderTrust.Unverified, new SenderAuthentication(spf, dkim, dmarc).Trust);
    }

    [Fact]
    public void Verified_when_something_passes()
    {
        Assert.Equal(
            SenderTrust.Verified,
            new SenderAuthentication(EmailAuthResult.Pass, EmailAuthResult.Fail, EmailAuthResult.None).Trust
        );
    }

    [Fact]
    public void Unknown_when_no_signals()
    {
        Assert.Equal(SenderTrust.Unknown, SenderAuthentication.Unknown.Trust);
    }

    [Fact]
    public void Parse_is_case_insensitive_and_defaults_to_Unknown()
    {
        Assert.Equal(EmailAuthResult.Pass, SenderAuthentication.Parse("pass"));
        Assert.Equal(EmailAuthResult.Unknown, SenderAuthentication.Parse("garbage"));
        Assert.Equal(EmailAuthResult.Unknown, SenderAuthentication.Parse(null));
    }
}
