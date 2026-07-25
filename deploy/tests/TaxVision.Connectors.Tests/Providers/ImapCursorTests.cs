using TaxVision.Connectors.Infrastructure.Providers.Imap;

namespace TaxVision.Connectors.Tests.Providers;

public class ImapCursorTests
{
    [Fact]
    public void ToString_WithoutBackfill_FormatsAsUidValidityColonLiveLastUid()
    {
        var cursor = new ImapCursor(123u, 456u, null, null);

        Assert.Equal("123:456", cursor.ToString());
    }

    [Fact]
    public void ToString_WithBackfillWindow_FormatsAsFourParts()
    {
        var cursor = new ImapCursor(123u, 999u, 456u, 800u);

        Assert.Equal("123:999:456:800", cursor.ToString());
    }

    [Fact]
    public void Parse_WithValidTwoPartCursor_RoundTrips()
    {
        var original = new ImapCursor(123u, 456u, null, null);

        var parsed = ImapCursor.Parse(original.ToString());

        Assert.Equal(original, parsed);
    }

    [Fact]
    public void Parse_WithValidFourPartCursor_RoundTrips()
    {
        var original = new ImapCursor(123u, 999u, 456u, 800u);

        var parsed = ImapCursor.Parse(original.ToString());

        Assert.Equal(original, parsed);
    }

    [Fact]
    public void Parse_LegacyTwoPartFormat_InterpretedAsLiveLastUidWithoutBackfill()
    {
        var parsed = ImapCursor.Parse("1:712");

        Assert.Equal(new ImapCursor(1u, 712u, null, null), parsed);
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
    [InlineData("abc:456:-:-")]
    [InlineData("123:abc:-:-")]
    public void Parse_WithMalformedCursor_ReturnsNull(string cursor)
    {
        Assert.Null(ImapCursor.Parse(cursor));
    }

    [Fact]
    public void Parse_WithZeroValues_ReturnsZeroCursor()
    {
        var parsed = ImapCursor.Parse("0:0");

        Assert.Equal(new ImapCursor(0u, 0u, null, null), parsed);
    }

    [Fact]
    public void Parse_FourPartWithDashes_ReturnsNullBackfillFields()
    {
        var parsed = ImapCursor.Parse("1:999:-:-");

        Assert.Equal(new ImapCursor(1u, 999u, null, null), parsed);
    }
}
