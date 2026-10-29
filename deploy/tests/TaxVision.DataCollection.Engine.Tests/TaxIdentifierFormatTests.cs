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

    [Theory]
    [InlineData("My SSN is 123-45-6789", true)]
    [InlineData("EIN 12-3456789", true)]
    [InlineData("id 123456789", true)]
    [InlineData("000000000", false)]
    [InlineData("1234-56-78901", false)]
    [InlineData("Order 1234567890", false)]
    [InlineData("Unit 12, floor 3, ref 123456", false)]
    [InlineData("١٢٣-٤٥-٦٧٨٩", false)]
    public void Tax_identifier_guard_matches_spec_patterns_on_digit_runs(string text, bool expected)
    {
        Assert.Equal(expected, TaxIdentifierFormat.LooksLikeTaxIdentifier(text));
    }

    [Theory]
    [InlineData("4111 1111 1111 1111", true)]
    [InlineData("4111-1111-1111-1111", true)]
    [InlineData("4111111111111111 5551234", true)]
    [InlineData("4111111111111112", false)]
    [InlineData("41111111111111115551234", false)]
    [InlineData("Call 4111 1111 now, then 1111 1111", false)]
    [InlineData("٤١١١١١١١١١١١١١١١", false)]
    public void Card_guard_needs_13_to_19_contiguous_digits_passing_luhn(string text, bool expected)
    {
        Assert.Equal(expected, SensitivePatternGuard.LooksLikeCardNumber(text));
    }
}
