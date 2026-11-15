using BuildingBlocks.TimeZones;
using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Tests;

/// <summary>"Faltan N días" se cuenta en el calendario de la oficina, no en días UTC (daba ±1).</summary>
public sealed class OfficeDaysTests
{
    private static readonly ResolvedTimeZone NewYork = ResolvedTimeZone.Resolve("America/New_York");

    [Fact]
    public void Days_are_counted_on_the_office_calendar()
    {
        // 22:00 del 17-nov en Nueva York (03:00Z del 18) hasta el 19-nov en Nueva York: faltan 2 días (en UTC daba 1).
        var now = new DateTime(2026, 11, 18, 3, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 11, 19, 15, 0, 0, DateTimeKind.Utc);

        Assert.Equal(2, OfficeDays.Until(end, now, NewYork));
    }

    [Fact]
    public void A_past_date_is_zero_days_away()
    {
        Assert.Equal(
            0,
            OfficeDays.Until(
                new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc),
                NewYork
            )
        );
    }
}
