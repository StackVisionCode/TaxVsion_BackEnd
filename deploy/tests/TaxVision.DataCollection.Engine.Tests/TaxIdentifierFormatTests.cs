using TaxVision.DataCollection.Engine.Formats;

namespace TaxVision.DataCollection.Engine.Tests;

public sealed class TaxIdentifierFormatTests
{
    [Theory]
    [InlineData("123-45-6789", TaxIdentifierKind.Ssn, true)]
    [InlineData("000-45-6789", TaxIdentifierKind.Ssn, false)]
    [InlineData("666-45-6789", TaxIdentifierKind.Ssn, false)]
    [InlineData("900-45-6789", TaxIdentifierKind.Ssn, false)]
    [InlineData("123-00-6789", TaxIdentifierKind.Ssn, false)]
    [InlineData("123-45-0000", TaxIdentifierKind.Ssn, false)]
    [InlineData("912-50-6789", TaxIdentifierKind.Itin, true)]
    [InlineData("912-65-6789", TaxIdentifierKind.Itin, true)]
    [InlineData("912-70-6789", TaxIdentifierKind.Itin, true)]
    [InlineData("912-88-6789", TaxIdentifierKind.Itin, true)]
    [InlineData("912-90-6789", TaxIdentifierKind.Itin, true)]
    [InlineData("912-92-6789", TaxIdentifierKind.Itin, true)]
    [InlineData("912-94-6789", TaxIdentifierKind.Itin, true)]
    [InlineData("912-99-6789", TaxIdentifierKind.Itin, true)]
    [InlineData("912-66-6789", TaxIdentifierKind.Itin, false)]
    [InlineData("812-50-6789", TaxIdentifierKind.Itin, false)]
    [InlineData("12-3456789", TaxIdentifierKind.Ein, true)]
    [InlineData("00-3456789", TaxIdentifierKind.Ein, false)]
    [InlineData("12-345678", TaxIdentifierKind.Ein, false)]
    public void Validates_tax_identifier_rules(string value, TaxIdentifierKind kind, bool expected)
    {
        Assert.Equal(expected, TaxIdentifierFormat.TryNormalize(value, kind, out _));
    }
}
