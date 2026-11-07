using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Tests.Domain;

public sealed class SignerFullNameTests
{
    private static SignerFullName Name() => SignerFullName.Create("Ana María Pérez").Value;

    [Theory]
    [InlineData("Ana María Pérez")]
    [InlineData("ana maría pérez")]
    [InlineData("  ANA MARÍA PÉREZ  ")]
    public void Matches_IgnoresCaseAndExtraSpaces(string typed)
    {
        Assert.True(Name().Matches(typed));
    }

    [Theory]
    [InlineData("Ana Perez")]
    [InlineData("Ana María  Pérez")]
    [InlineData("Ana Maria Perez")]
    [InlineData("")]
    [InlineData(null)]
    public void Matches_DifferentName_False(string? typed)
    {
        Assert.False(Name().Matches(typed));
    }
}
