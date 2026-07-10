using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Tests.Domain;

public sealed class ValueObjectTests
{
    // -------------------- SignerEmail --------------------

    [Fact]
    public void SignerEmail_normalizes_to_lowercase_and_trims()
    {
        var result = SignerEmail.Create("  Signer.One@Example.COM ");

        Assert.True(result.IsSuccess);
        Assert.Equal("signer.one@example.com", result.Value.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-at-sign.example.com")]
    [InlineData("@nolocal.com")]
    [InlineData("noat@")]
    [InlineData("two@@ats.com")]
    [InlineData("dot@nodotdomain")]
    public void SignerEmail_rejects_invalid_formats(string raw)
    {
        Assert.True(SignerEmail.Create(raw).IsFailure);
    }

    // -------------------- SignerFullName --------------------

    [Fact]
    public void SignerFullName_preserves_case_and_trims()
    {
        var result = SignerFullName.Create("  Jane A. Doe  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("Jane A. Doe", result.Value.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    public void SignerFullName_rejects_short_or_empty(string raw)
    {
        Assert.True(SignerFullName.Create(raw).IsFailure);
    }

    // -------------------- DocumentHash --------------------

    [Fact]
    public void DocumentHash_accepts_uppercase_hex_and_normalizes_to_lowercase()
    {
        var uppercase = new string('A', 64);
        var result = DocumentHash.Create(uppercase);

        Assert.True(result.IsSuccess);
        Assert.Equal(new string('a', 64), result.Value.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("tooshort")]
    [InlineData("znotsix4charshexznotsix4charshexznotsix4charshexznotsix4charshxz")]
    public void DocumentHash_rejects_invalid_hashes(string raw)
    {
        Assert.True(DocumentHash.Create(raw).IsFailure);
    }

    // -------------------- FieldPosition --------------------

    [Fact]
    public void FieldPosition_accepts_values_within_unit_square()
    {
        var result = FieldPosition.Create(page: 1, x: 0.1, y: 0.2, width: 0.3, height: 0.1);
        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData(0, 0.1, 0.1, 0.1, 0.1)] // page < 1
    [InlineData(1, -0.1, 0.1, 0.1, 0.1)] // negative x
    [InlineData(1, 0.1, 0.1, 0.0, 0.1)] // width = 0
    [InlineData(1, 0.9, 0.9, 0.2, 0.1)] // overflow x + width
    public void FieldPosition_rejects_out_of_bounds(int page, double x, double y, double w, double h)
    {
        Assert.True(FieldPosition.Create(page, x, y, w, h).IsFailure);
    }
}
