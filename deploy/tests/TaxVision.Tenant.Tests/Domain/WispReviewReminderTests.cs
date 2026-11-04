using TaxVision.Tenant.Domain.Compliance;

namespace TaxVision.Tenant.Tests.Domain;

/// <summary>
/// Recordatorio anual del WISP en el agregado: vence a los 12 meses de la última revisión (o de la adopción);
/// DueSoon una vez a ≤ 30 días, Overdue al vencer y cada 30 días; nada sin fechas; ciclo nuevo si cambian.
/// </summary>
public sealed class WispReviewReminderTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly DateTime SavedAt = new(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly DueOn = new(2026, 12, 1);

    [Fact]
    public void Without_wisp_dates_there_is_never_a_reminder()
    {
        var compliance = WithWisp(adopted: null, reviewed: null);

        Assert.Null(compliance.WispReviewDueOn);
        Assert.Null(compliance.RegisterWispReviewReminder(new DateOnly(2026, 11, 15)));
        Assert.Null(compliance.RegisterWispReviewReminder(new DateOnly(2030, 1, 1)));
        Assert.Null(compliance.WispReminderSentOn);
    }

    [Fact]
    public void The_review_falls_due_twelve_months_after_the_last_review_or_else_the_adoption()
    {
        Assert.Equal(DueOn, WithWisp("2025-12-01", null).WispReviewDueOn);
        Assert.Equal(DueOn, WithWisp("2024-01-10", "2025-12-01").WispReviewDueOn);
    }

    [Fact]
    public void Due_soon_starts_thirty_days_before_and_is_sent_once()
    {
        var compliance = WithWisp("2025-12-01", null);

        Assert.Null(compliance.RegisterWispReviewReminder(new DateOnly(2026, 10, 31)));

        var reminder = compliance.RegisterWispReviewReminder(new DateOnly(2026, 11, 1));
        Assert.Equal(new WispReviewReminder(DueOn, WispReviewReminderStage.DueSoon), reminder);
        Assert.Equal(DueOn, compliance.WispReminderDueOn);
        Assert.Equal(WispReviewReminderStage.DueSoon, compliance.WispReminderStage);
        Assert.Equal(new DateOnly(2026, 11, 1), compliance.WispReminderSentOn);

        Assert.Null(compliance.RegisterWispReviewReminder(new DateOnly(2026, 11, 1)));
        Assert.Null(compliance.RegisterWispReviewReminder(new DateOnly(2026, 11, 30)));
    }

    [Fact]
    public void Overdue_is_sent_on_the_due_date_and_again_every_thirty_days()
    {
        var compliance = WithWisp("2025-12-01", null);
        compliance.RegisterWispReviewReminder(new DateOnly(2026, 11, 1));

        Assert.Equal(WispReviewReminderStage.Overdue, compliance.RegisterWispReviewReminder(DueOn)!.Stage);
        Assert.Null(compliance.RegisterWispReviewReminder(DueOn));
        Assert.Null(compliance.RegisterWispReviewReminder(new DateOnly(2026, 12, 30)));

        var again = compliance.RegisterWispReviewReminder(new DateOnly(2026, 12, 31));
        Assert.Equal(new WispReviewReminder(DueOn, WispReviewReminderStage.Overdue), again);
        Assert.Null(compliance.RegisterWispReviewReminder(new DateOnly(2027, 1, 29)));
        Assert.NotNull(compliance.RegisterWispReviewReminder(new DateOnly(2027, 1, 30)));
    }

    [Fact]
    public void An_office_first_seen_already_overdue_gets_overdue_right_away()
    {
        var compliance = WithWisp("2024-03-01", null);

        var reminder = compliance.RegisterWispReviewReminder(new DateOnly(2026, 11, 3));

        Assert.Equal(new WispReviewReminder(new DateOnly(2025, 3, 1), WispReviewReminderStage.Overdue), reminder);
    }

    [Fact]
    public void Marking_it_reviewed_restarts_the_cycle()
    {
        var compliance = WithWisp("2025-12-01", null);
        compliance.RegisterWispReviewReminder(new DateOnly(2026, 12, 5));

        var reviewedAt = new DateTime(2026, 12, 5, 15, 0, 0, DateTimeKind.Utc);
        compliance.Update(Draft("2025-12-01", "2026-12-05"), EfinChange.Keep, ActorId, reviewedAt);

        Assert.Equal(new DateOnly(2027, 12, 5), compliance.WispReviewDueOn);
        Assert.Null(compliance.RegisterWispReviewReminder(new DateOnly(2026, 12, 6)));
        Assert.Null(compliance.RegisterWispReviewReminder(new DateOnly(2027, 1, 4)));

        var next = compliance.RegisterWispReviewReminder(new DateOnly(2027, 11, 5));
        Assert.Equal(new WispReviewReminder(new DateOnly(2027, 12, 5), WispReviewReminderStage.DueSoon), next);
    }

    [Fact]
    public void The_reminder_state_is_separate_from_the_admin_save()
    {
        var compliance = WithWisp("2025-12-01", null);
        var updatedAt = compliance.UpdatedAtUtc;
        compliance.RegisterWispReviewReminder(new DateOnly(2026, 11, 1));
        Assert.Equal(updatedAt, compliance.UpdatedAtUtc);

        compliance.Update(Draft("2025-12-01", null) with { RetentionYears = 7 }, EfinChange.Keep, ActorId, SavedAt);

        Assert.Equal(WispReviewReminderStage.DueSoon, compliance.WispReminderStage);
        Assert.Null(compliance.RegisterWispReviewReminder(new DateOnly(2026, 11, 2)));
    }

    [Theory]
    [InlineData("America/Los_Angeles", 2026, 11, 30)]
    [InlineData("Etc/UTC", 2026, 12, 1)]
    [InlineData("Asia/Tokyo", 2026, 12, 1)]
    [InlineData("Not/AZone", 2026, 12, 1)]
    [InlineData(null, 2026, 12, 1)]
    public void Today_is_read_in_the_office_time_zone_falling_back_to_utc(
        string? timeZoneId,
        int year,
        int month,
        int day
    )
    {
        var nowUtc = new DateTime(2026, 12, 1, 3, 0, 0, DateTimeKind.Utc);

        Assert.Equal(new DateOnly(year, month, day), WispReviewSchedule.OfficeToday(nowUtc, timeZoneId));
    }

    [Fact]
    public void The_same_instant_is_still_due_soon_in_a_western_office()
    {
        var nowUtc = new DateTime(2026, 12, 1, 3, 0, 0, DateTimeKind.Utc);
        var western = WithWisp("2025-12-01", null);
        var utc = WithWisp("2025-12-01", null);

        var westernStage = western.RegisterWispReviewReminder(
            WispReviewSchedule.OfficeToday(nowUtc, "America/Los_Angeles")
        );
        var utcStage = utc.RegisterWispReviewReminder(WispReviewSchedule.OfficeToday(nowUtc, "Etc/UTC"));

        Assert.Equal(WispReviewReminderStage.DueSoon, westernStage!.Stage);
        Assert.Equal(WispReviewReminderStage.Overdue, utcStage!.Stage);
    }

    private static OfficeCompliance WithWisp(string? adopted, string? reviewed)
    {
        var compliance = OfficeCompliance.CreateEmpty(TenantId);
        compliance.Update(Draft(adopted, reviewed), EfinChange.Keep, ActorId, SavedAt);
        return compliance;
    }

    private static OfficeComplianceDraft Draft(string? adopted, string? reviewed) =>
        new(null, adopted, reviewed, null, null, null);
}
