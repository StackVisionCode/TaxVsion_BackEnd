using TaxVision.Postmaster.Domain.ValueObjects;

namespace TaxVision.Postmaster.Tests.Domain;

public sealed class EnhancedStatusCodeTests
{
    [Theory]
    [InlineData("5.1.1", "5.1.1")]
    [InlineData(" 4.4.7 ", "4.4.7")]
    [InlineData("550 5.1.1 <gone@example.com>: user unknown", "5.1.1")]
    [InlineData("mx.example.com: 552-5.2.2 The email account is over quota", "5.2.2")]
    [InlineData("Remote Server returned '550 5.4.310 DNS domain does not exist'", "5.4.310")]
    [InlineData("smtp; 550 5.1.10 RESOLVER.ADR.RecipientNotFound", "5.1.10")]
    public void Find_reads_the_first_code_in_the_text(string text, string expected)
    {
        Assert.Equal(expected, EnhancedStatusCode.Find(text)?.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("550 user unknown")]
    // Ni una IP ni una versión son códigos.
    [InlineData("connection to 192.5.1.1 failed")]
    [InlineData("relay 5.1.1.4 refused")]
    [InlineData("3.1.1 is not a valid class")]
    public void Find_returns_null_without_a_code(string? text)
    {
        Assert.Null(EnhancedStatusCode.Find(text));
    }

    [Fact]
    public void Find_knows_permanent_from_temporary()
    {
        Assert.True(EnhancedStatusCode.Find("5.1.1")!.IsPermanent);
        Assert.False(EnhancedStatusCode.Find("4.2.2")!.IsPermanent);
    }
}
