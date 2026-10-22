using TaxVision.Postmaster.Infrastructure.Providers.Smtp;

namespace TaxVision.Postmaster.Tests.Providers;

public sealed class SmtpRelayAcceptanceTests
{
    [Theory]
    [InlineData("OK id=1oQsWu-4XUKtC-0n", "1oQsWu-4XUKtC-0n")]
    [InlineData("2.0.0 Ok: queued as ABC id=xyz123.", "xyz123")]
    [InlineData("OK ID=abc", "abc")]
    public void ExtractReference_reads_the_relay_id(string response, string expected)
    {
        Assert.Equal(expected, SmtpRelayAcceptance.ExtractReference(response));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2.0.0 Ok: queued as 4F2A1")]
    [InlineData("OK id=")]
    public void ExtractReference_returns_null_without_an_id(string? response)
    {
        Assert.Null(SmtpRelayAcceptance.ExtractReference(response));
    }
}
