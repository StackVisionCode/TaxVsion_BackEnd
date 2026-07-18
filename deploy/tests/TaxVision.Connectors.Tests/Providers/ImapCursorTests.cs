using TaxVision.Connectors.Infrastructure.Providers.Imap;

namespace TaxVision.Connectors.Tests.Providers;

public class ImapCursorTests
{
    [Fact]
    public void ToString_FormatsAsUidValidityColonLastUid()
    {
        var cursor = new ImapCursor(123u, 456u);

        Assert.Equal("123:456", cursor.ToString());
    }

    [Fact]
    public void Parse_WithValidCursor_RoundTrips()
    {
        var original = new ImapCursor(123u, 456u);

        var parsed = ImapCursor.Parse(original.ToString());

        Assert.Equal(original, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_WithNullOrBlank_ReturnsNull(string? cursor)
    {
        Assert.Null(ImapCursor.Parse(cursor));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("123:456:789")]
    [InlineData("abc:456")]
    [InlineData("123:abc")]
    [InlineData(":456")]
    [InlineData("123:")]
    public void Parse_WithMalformedCursor_ReturnsNull(string cursor)
    {
        Assert.Null(ImapCursor.Parse(cursor));
    }

    [Fact]
    public void Parse_WithZeroValues_ReturnsZeroCursor()
    {
        var parsed = ImapCursor.Parse("0:0");

        Assert.Equal(new ImapCursor(0u, 0u), parsed);
    }
}
