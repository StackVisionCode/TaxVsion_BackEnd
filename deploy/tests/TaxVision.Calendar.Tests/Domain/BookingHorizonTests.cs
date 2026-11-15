using TaxVision.Calendar.Domain.Scheduling;
using Xunit;

namespace TaxVision.Calendar.Tests.Domain;

/// <summary>
/// P23 (D-TZ02-6): "hasta N días" llega al fin del último día local, no a la hora actual de ese día. Así el último
/// día ofrece su tarde aunque se consulte de mañana.
/// </summary>
public sealed class BookingHorizonTests
{
    [Fact]
    public void The_last_bookable_day_ends_at_local_midnight()
    {
        // 15:00Z del 12-nov = 10:00 en Nueva York; 30 días → fin del 12-dic local = 05:00Z del 13-dic.
        var now = new DateTime(2026, 11, 12, 15, 0, 0, DateTimeKind.Utc);

        Assert.Equal(
            new DateTime(2026, 12, 13, 5, 0, 0, DateTimeKind.Utc),
            BookingHorizon.EndUtc(now, 30, "America/New_York")
        );
    }

    [Fact]
    public void The_local_day_counts_even_when_utc_already_moved_on()
    {
        // 03:00Z del 13-nov = 22:00 del 12-nov en Nueva York: "hoy" sigue siendo el 12.
        var now = new DateTime(2026, 11, 13, 3, 0, 0, DateTimeKind.Utc);

        Assert.Equal(
            new DateTime(2026, 11, 14, 5, 0, 0, DateTimeKind.Utc),
            BookingHorizon.EndUtc(now, 1, "America/New_York")
        );
    }

    [Fact]
    public void An_unknown_zone_uses_utc_days()
    {
        var now = new DateTime(2026, 11, 12, 15, 0, 0, DateTimeKind.Utc);

        Assert.Equal(new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc), BookingHorizon.EndUtc(now, 1, "Mars/Base"));
    }
}
