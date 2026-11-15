using BuildingBlocks.TimeZones;
using TaxVision.Subscription.Application.Subscriptions;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>
/// P28: los avisos de renovación y de fin de acceso cuentan días en el calendario de la oficina y salen a partir de
/// las 9:00 locales (una sola pasada por día: la franja de 9:00 a 9:59).
/// </summary>
public sealed class OfficeNoticeTimeTests
{
    private static readonly ResolvedTimeZone NewYork = ResolvedTimeZone.Resolve("America/New_York");

    [Fact]
    public void Days_are_counted_on_the_office_calendar()
    {
        // 22:00 del 17-nov en Nueva York (03:00Z del 18) hasta el 24-nov: faltan 7 días (en UTC daba 6).
        Assert.Equal(
            7,
            OfficeNoticeTime.DaysUntil(
                new DateTime(2026, 11, 24, 15, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 11, 18, 3, 0, 0, DateTimeKind.Utc),
                NewYork
            )
        );
    }

    // Aceptación de la Fase 7: el día en que termina el horario de verano, el aviso "en 7 días" sale a las 9:00
    // locales (14:00Z en EST), no a las 8:00, y cuenta 7 días del calendario de la oficina.
    [Fact]
    public void The_seven_day_notice_goes_out_at_nine_on_the_day_dst_ends()
    {
        var renewal = new DateTime(2026, 11, 8, 14, 0, 0, DateTimeKind.Utc);

        Assert.False(OfficeNoticeTime.IsSendHour(new DateTime(2026, 11, 1, 13, 0, 0, DateTimeKind.Utc), NewYork));
        var nine = new DateTime(2026, 11, 1, 14, 0, 0, DateTimeKind.Utc);
        Assert.True(OfficeNoticeTime.IsSendHour(nine, NewYork));
        Assert.Equal(7, OfficeNoticeTime.DaysUntil(renewal, nine, NewYork));
    }

    [Theory]
    [InlineData(13, 30, false)] // 8:30 en Nueva York (EST)
    [InlineData(14, 0, true)] // 9:00
    [InlineData(14, 59, true)] // 9:59
    [InlineData(15, 0, false)] // 10:00: la pasada siguiente ya no reenvía
    public void Notices_go_out_in_the_nine_oclock_office_hour(int hourUtc, int minuteUtc, bool expected)
    {
        Assert.Equal(
            expected,
            OfficeNoticeTime.IsSendHour(new DateTime(2026, 11, 18, hourUtc, minuteUtc, 0, DateTimeKind.Utc), NewYork)
        );
    }
}
