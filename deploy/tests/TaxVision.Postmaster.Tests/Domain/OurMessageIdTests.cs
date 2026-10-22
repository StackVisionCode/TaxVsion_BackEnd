using TaxVision.Postmaster.Domain.ValueObjects;

namespace TaxVision.Postmaster.Tests.Domain;

public sealed class OurMessageIdTests
{
    private static readonly Guid SentMessageId = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

    [Fact]
    public void For_uses_the_sender_domain()
    {
        var id = OurMessageId.For(SentMessageId, "No-Reply@TaxOffix.com");

        Assert.Equal("pm.0f8fad5bd9cb469fa16570867728950e@taxoffix.com", id.ToString());
        Assert.Equal("0f8fad5bd9cb469fa16570867728950e", id.CorrelationHeaderValue);
    }

    [Theory]
    [InlineData("pm.0f8fad5bd9cb469fa16570867728950e@taxoffix.com")]
    [InlineData("<pm.0f8fad5bd9cb469fa16570867728950e@taxoffix.com>")]
    [InlineData("  <pm.0f8fad5bd9cb469fa16570867728950e@taxoffix.com>  ")]
    public void TryParse_reads_our_own_id(string value)
    {
        Assert.True(OurMessageId.TryParse(value, out var parsed));
        Assert.Equal(SentMessageId, parsed!.SentMessageId);
        Assert.Equal("taxoffix.com", parsed.Domain);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<CAJxyz123@mail.gmail.com>")]
    // Id de campaña: no es de Postmaster aunque se parezca.
    [InlineData("tvc.0f8fad5bd9cb469fa16570867728950e@taxoffix.com")]
    [InlineData("pm.not-a-guid@taxoffix.com")]
    [InlineData("pm.0f8fad5bd9cb469fa16570867728950e")]
    [InlineData("pm.0f8fad5bd9cb469fa16570867728950e@")]
    [InlineData("PM.0f8fad5bd9cb469fa16570867728950e@taxoffix.com")]
    public void TryParse_rejects_ids_that_are_not_ours(string? value)
    {
        Assert.False(OurMessageId.TryParse(value, out var parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void TryParseCorrelationHeader_reads_the_header_value()
    {
        Assert.True(OurMessageId.TryParseCorrelationHeader(" 0f8fad5bd9cb469fa16570867728950e ", out var id));
        Assert.Equal(SentMessageId, id);
        Assert.False(OurMessageId.TryParseCorrelationHeader("something-else", out _));
        Assert.False(OurMessageId.TryParseCorrelationHeader(null, out _));
    }
}
