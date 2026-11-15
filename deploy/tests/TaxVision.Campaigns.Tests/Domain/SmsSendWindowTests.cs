using BuildingBlocks.TimeZones;
using TaxVision.Campaigns.Domain.Scheduling;
using Xunit;

namespace TaxVision.Campaigns.Tests.Domain;

/// <summary>
/// P12: los SMS de campañas salen entre las 08:00 y las 21:00 de la zona del destinatario (o de la oficina). Un slot
/// fuera de la ventana espera a la próxima apertura y el agendado sigue avanzando desde su slot original.
/// </summary>
public sealed class SmsSendWindowTests
{
    private static readonly ResolvedTimeZone NewYork = ResolvedTimeZone.Resolve("America/New_York");
    private static readonly TimeOnly Opens = new(8, 0);
    private static readonly TimeOnly Closes = new(21, 0);

    [Fact]
    public void Inside_the_window_there_is_nothing_to_wait_for()
    {
        // 12:00 en Nueva York.
        Assert.Null(
            SmsSendWindow.NextOpening(new DateTime(2026, 11, 18, 17, 0, 0, DateTimeKind.Utc), NewYork, Opens, Closes)
        );
    }

    [Fact]
    public void Early_morning_waits_until_eight_the_same_day()
    {
        // 6:00 en Nueva York (11:00Z) → 8:00 (13:00Z).
        Assert.Equal(
            new DateTime(2026, 11, 18, 13, 0, 0, DateTimeKind.Utc),
            SmsSendWindow.NextOpening(new DateTime(2026, 11, 18, 11, 0, 0, DateTimeKind.Utc), NewYork, Opens, Closes)
        );
    }

    [Fact]
    public void Night_waits_until_eight_the_next_day()
    {
        // 23:00 del 18 en Nueva York (04:00Z del 19) → 8:00 del 19 (13:00Z).
        Assert.Equal(
            new DateTime(2026, 11, 19, 13, 0, 0, DateTimeKind.Utc),
            SmsSendWindow.NextOpening(new DateTime(2026, 11, 19, 4, 0, 0, DateTimeKind.Utc), NewYork, Opens, Closes)
        );
    }

    [Fact]
    public void A_deferred_schedule_is_not_claimable_until_the_opening_and_then_advances_from_its_slot()
    {
        var slot = new DateTime(2026, 11, 19, 4, 0, 0, DateTimeKind.Utc);
        var schedule = CampaignSchedule
            .CreateRecurring(Guid.NewGuid(), Guid.NewGuid(), slot, RecurrenceFrequency.Daily, null, [])
            .Value;
        var opening = new DateTime(2026, 11, 19, 13, 0, 0, DateTimeKind.Utc);

        schedule.DeferTo(opening);

        Assert.False(schedule.IsClaimable(opening.AddMinutes(-1)));
        Assert.True(schedule.IsClaimable(opening));
        schedule.MarkFired(Guid.Empty, opening);
        Assert.Null(schedule.DeferredUntilUtc);
        // El siguiente sale del slot original (04:00Z del 20), no de la hora diferida.
        Assert.Equal(slot.AddDays(1), schedule.NextFireAtUtc);
    }
}
