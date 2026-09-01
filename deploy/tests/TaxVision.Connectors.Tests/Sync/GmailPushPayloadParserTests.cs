using TaxVision.Connectors.Application.Sync;

namespace TaxVision.Connectors.Tests.Sync;

/// <summary>
/// Regresión del 400 en el webhook de Gmail: el push real trae <c>historyId</c> como NÚMERO JSON.
/// Deserializarlo a un string rompía y cada push se rechazaba con 400 (el correo entrante nunca entraba).
/// </summary>
public sealed class GmailPushPayloadParserTests
{
    [Fact]
    public void Parse_NumericHistoryId_NormalizesToString()
    {
        var result = GmailPushPayloadParser.Parse(
            "{\"emailAddress\":\"castillogarcia.gtl@gmail.com\",\"historyId\":9876543210}"
        );

        Assert.NotNull(result);
        Assert.Equal("castillogarcia.gtl@gmail.com", result!.Value.EmailAddress);
        Assert.Equal("9876543210", result.Value.HistoryId);
    }

    [Fact]
    public void Parse_StringHistoryId_AlsoWorks()
    {
        var result = GmailPushPayloadParser.Parse("{\"emailAddress\":\"a@b.com\",\"historyId\":\"12345\"}");

        Assert.NotNull(result);
        Assert.Equal("a@b.com", result!.Value.EmailAddress);
        Assert.Equal("12345", result.Value.HistoryId);
    }

    [Fact]
    public void Parse_LargeUint64HistoryId_DoesNotOverflow()
    {
        // historyId es uint64; un valor cercano al máximo no debe perderse ni tirar.
        var result = GmailPushPayloadParser.Parse("{\"emailAddress\":\"a@b.com\",\"historyId\":18446744073709551615}");

        Assert.NotNull(result);
        Assert.Equal("18446744073709551615", result!.Value.HistoryId);
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsNull()
    {
        Assert.Null(GmailPushPayloadParser.Parse("not-json"));
    }

    [Fact]
    public void Parse_MissingFields_ReturnsPayloadWithNulls()
    {
        var result = GmailPushPayloadParser.Parse("{}");

        Assert.NotNull(result);
        Assert.Null(result!.Value.EmailAddress);
        Assert.Null(result.Value.HistoryId);
    }
}
