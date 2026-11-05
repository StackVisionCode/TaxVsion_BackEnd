using TaxVision.Sms.Domain.Consent;

namespace TaxVision.Sms.Tests.Domain;

public sealed class SmsKeywordParserTests
{
    [Theory]
    [InlineData("STOP")]
    [InlineData("stop")]
    [InlineData("Unsubscribe")]
    [InlineData("cancel.")]
    [InlineData("Quit ")]
    [InlineData("STOPALL")]
    [InlineData("Stop please")]
    [InlineData("end")]
    [InlineData("revoke")]
    [InlineData("optout")]
    [InlineData("Opt out")]
    [InlineData("OPT OUT please")]
    [InlineData("Alto")]
    [InlineData("parar")]
    [InlineData("BÁJA!")]
    [InlineData("Cancelar")]
    [InlineData("detener.")]
    public void Parse_OptOutVariants_ReturnStop(string body) =>
        Assert.Equal(SmsKeyword.Stop, SmsKeywordParser.Parse(body));

    [Theory]
    [InlineData("START")]
    [InlineData("unstop")]
    [InlineData("Yes!")]
    public void Parse_OptInVariants_ReturnStart(string body) =>
        Assert.Equal(SmsKeyword.Start, SmsKeywordParser.Parse(body));

    [Theory]
    [InlineData("HELP")]
    [InlineData("info?")]
    [InlineData("Ayuda")]
    public void Parse_HelpVariants_ReturnHelp(string body) =>
        Assert.Equal(SmsKeyword.Help, SmsKeywordParser.Parse(body));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("si")]
    [InlineData("Thanks, see you")]
    [InlineData("opt")]
    [InlineData("I will stop by tomorrow")]
    public void Parse_OtherText_ReturnsUnknown(string? body) =>
        Assert.Equal(SmsKeyword.Unknown, SmsKeywordParser.Parse(body));
}
