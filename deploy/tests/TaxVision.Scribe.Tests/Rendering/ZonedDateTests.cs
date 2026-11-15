using BuildingBlocks.TimeZones;
using TaxVision.Scribe.Infrastructure.Rendering;

namespace TaxVision.Scribe.Tests.Rendering;

/// <summary>P14: plazos de una hora o menos en relativo; los de días, relativo + fecha con zona.</summary>
public sealed class ZonedDateTests
{
    private static readonly ResolvedTimeZone NewYork = ResolvedTimeZone.Resolve("America/New_York");
    private static readonly DateTime Now = new(2026, 11, 15, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_short_deadline_is_relative_in_minutes()
    {
        var date = ZonedDate.Instant(Now.AddMinutes(15), NewYork, null);

        Assert.Equal("in 15 minutes", date.Deadline(Now));
    }

    [Fact]
    public void A_deadline_of_days_is_relative_plus_the_date_in_the_zone()
    {
        var date = ZonedDate.Instant(new DateTime(2026, 11, 18, 14, 0, 0, DateTimeKind.Utc), NewYork, null);

        Assert.Equal("in 3 days, on Wed, Nov 18, 2026, 9:00 AM · New York time, GMT-5", date.Deadline(Now));
    }

    [Fact]
    public void A_deadline_in_spanish()
    {
        var date = ZonedDate.Instant(Now.AddMinutes(15), NewYork, "es");

        Assert.Equal("en 15 minutos", date.Deadline(Now));
    }
}
