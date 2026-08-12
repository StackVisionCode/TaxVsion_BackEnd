using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Tests.Domain;

public sealed class ReminderTimeZoneTests
{
    [Fact]
    public void Create_AceptaZonaIanaReal()
    {
        var result = ReminderTimeZone.Create("America/Santo_Domingo");

        Assert.True(result.IsSuccess);
        Assert.Equal("America/Santo_Domingo", result.Value.Value);
    }

    [Theory]
    [InlineData("Fake/Zone")]
    [InlineData("")]
    [InlineData(null)]
    public void Create_RechazaZonaInvalida(string? ianaId)
    {
        var result = ReminderTimeZone.Create(ianaId);

        Assert.True(result.IsFailure);
        Assert.Equal("Reminder.TimeZone.Invalid", result.Error.Code);
    }
}
