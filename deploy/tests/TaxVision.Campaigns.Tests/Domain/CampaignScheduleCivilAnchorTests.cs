using BuildingBlocks.TimeZones;
using TaxVision.Campaigns.Domain.Scheduling;
using Xunit;

namespace TaxVision.Campaigns.Tests.Domain;

/// <summary>
/// P10: un agendado diario, semanal o mensual avanza en hora local de la oficina. "Todos los días a las 8:30" sigue
/// a las 8:30 después del cambio de horario; un mensual del 31 vuelve al 31 tras un mes corto.
/// </summary>
public sealed class CampaignScheduleCivilAnchorTests
{
    private static readonly ResolvedTimeZone NewYork = ResolvedTimeZone.Resolve("America/New_York");

    [Fact]
    public void A_daily_schedule_keeps_eight_thirty_across_the_dst_change()
    {
        // 8:30 del 31-oct en Nueva York (EDT, 12:30Z). El 1-nov termina el horario de verano.
        var first = new DateTime(2026, 10, 31, 12, 30, 0, DateTimeKind.Utc);
        var schedule = Recurring(first, RecurrenceFrequency.Daily);
        schedule.AnchorToOfficeZone(NewYork);

        schedule.MarkFired(Guid.Empty, first);

        // 8:30 del 1-nov en EST = 13:30Z (sumando 24 h salía 12:30Z, las 7:30 locales).
        Assert.Equal(new DateTime(2026, 11, 1, 13, 30, 0, DateTimeKind.Utc), schedule.NextFireAtUtc);
    }

    // Aceptación de la Fase 7: también en el cambio de primavera la campaña sale a las 8:30 locales.
    [Fact]
    public void A_daily_schedule_keeps_eight_thirty_across_the_spring_change()
    {
        // 8:30 del 13-mar-2027 en Nueva York (EST, 13:30Z). El 14-mar empieza el horario de verano.
        var first = new DateTime(2027, 3, 13, 13, 30, 0, DateTimeKind.Utc);
        var schedule = Recurring(first, RecurrenceFrequency.Daily);
        schedule.AnchorToOfficeZone(NewYork);

        schedule.MarkFired(Guid.Empty, first);

        // 8:30 del 14-mar en EDT = 12:30Z.
        Assert.Equal(new DateTime(2027, 3, 14, 12, 30, 0, DateTimeKind.Utc), schedule.NextFireAtUtc);
    }

    [Fact]
    public void A_monthly_schedule_on_the_31st_returns_to_the_31st_after_a_short_month()
    {
        // 9:00 del 31-ene en Nueva York (EST, 14:00Z).
        var first = new DateTime(2027, 1, 31, 14, 0, 0, DateTimeKind.Utc);
        var schedule = Recurring(first, RecurrenceFrequency.Monthly);
        schedule.AnchorToOfficeZone(NewYork);

        schedule.MarkFired(Guid.Empty, first);
        Assert.Equal(new DateTime(2027, 2, 28, 14, 0, 0, DateTimeKind.Utc), schedule.NextFireAtUtc);

        schedule.MarkFired(Guid.Empty, schedule.NextFireAtUtc!.Value);
        // 31-mar a las 9:00 EDT = 13:00Z (con AddMonths quedaba en el 28).
        Assert.Equal(new DateTime(2027, 3, 31, 13, 0, 0, DateTimeKind.Utc), schedule.NextFireAtUtc);
    }

    /// <summary>Un agendado vivo sin ancla toma la de su próximo envío; no se mueven datos.</summary>
    [Fact]
    public void A_live_schedule_takes_the_anchor_of_its_next_run()
    {
        var schedule = Recurring(new DateTime(2026, 11, 20, 13, 30, 0, DateTimeKind.Utc), RecurrenceFrequency.Weekly);

        schedule.AnchorToOfficeZone(NewYork);

        Assert.Equal("America/New_York", schedule.TimeZoneId);
        Assert.Equal(new TimeOnly(8, 30), schedule.LocalTimeOfDay);
        Assert.Equal(20, schedule.AnchorDay);
    }

    [Fact]
    public void Hourly_and_custom_schedules_stay_as_durations()
    {
        var schedule = Recurring(new DateTime(2026, 11, 20, 13, 30, 0, DateTimeKind.Utc), RecurrenceFrequency.Hourly);

        schedule.AnchorToOfficeZone(NewYork);

        Assert.Null(schedule.TimeZoneId);
    }

    private static CampaignSchedule Recurring(DateTime first, RecurrenceFrequency frequency) =>
        CampaignSchedule.CreateRecurring(Guid.NewGuid(), Guid.NewGuid(), first, frequency, null, []).Value;
}
