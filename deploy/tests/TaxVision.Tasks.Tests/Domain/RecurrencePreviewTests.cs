using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Domain;

/// <summary>Las fechas y el resumen de la vista previa: la tabla del doc 05 §5.3, con "Starts on" = 23-nov-2026.</summary>
public sealed class RecurrencePreviewTests
{
    private static readonly DateOnly Starts = new(2026, 11, 23);

    private static IReadOnlyList<DateOnly> Dates(
        string rule,
        RecurrenceMode mode = RecurrenceMode.FixedSchedule,
        int count = 4
    ) =>
        RecurrencePreview
            .Compute(RecurrenceRule.Create(rule, "America/New_York").Value, mode, Starts, null, null, count)
            .Dates;

    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Fact]
    public void The_first_date_ignores_the_interval_and_the_next_ones_follow_it()
    {
        Assert.Equal(
            [D(2026, 11, 23), D(2026, 12, 7), D(2026, 12, 21), D(2027, 1, 4)],
            Dates("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO")
        );
    }

    [Fact]
    public void Quarterly_payroll_lands_on_the_last_day_including_april_30()
    {
        Assert.Equal(
            [D(2027, 1, 31), D(2027, 4, 30), D(2027, 7, 31), D(2027, 10, 31)],
            Dates("FREQ=YEARLY;BYMONTH=1,4,7,10;BYMONTHDAY=-1")
        );
    }

    [Theory]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=-1", "2026-11-30,2026-12-31,2027-01-31,2027-02-28")]
    [InlineData("FREQ=MONTHLY;BYDAY=-1FR", "2026-11-27,2026-12-25,2027-01-29,2027-02-26")]
    [InlineData("FREQ=YEARLY;BYMONTH=1,4,6,9;BYMONTHDAY=15", "2027-01-15,2027-04-15,2027-06-15,2027-09-15")]
    [InlineData("FREQ=WEEKLY;BYDAY=MO,TH", "2026-11-23,2026-11-26,2026-11-30,2026-12-03")]
    public void Each_builder_option_produces_the_dates_of_the_table(string rule, string expected)
    {
        Assert.Equal(expected.Split(',').Select(DateOnly.Parse), Dates(rule));
    }

    [Fact]
    public void After_completion_starts_on_the_chosen_day_and_assumes_each_one_is_completed_on_time()
    {
        Assert.Equal(
            [D(2026, 11, 23), D(2027, 2, 21), D(2027, 5, 22)],
            Dates("FREQ=DAILY;INTERVAL=90", RecurrenceMode.AfterCompletion, 3)
        );
    }

    [Fact]
    public void The_end_and_the_limit_cut_the_list()
    {
        var rule = RecurrenceRule.Create("FREQ=MONTHLY;BYMONTHDAY=15", "America/New_York").Value;

        Assert.Equal(
            2,
            RecurrencePreview.Compute(rule, RecurrenceMode.FixedSchedule, Starts, D(2027, 1, 20), null, 5).Dates.Count
        );
        Assert.Single(RecurrencePreview.Compute(rule, RecurrenceMode.FixedSchedule, Starts, null, 1, 5).Dates);
    }

    [Fact]
    public void February_29_warns_that_it_only_happens_in_leap_years()
    {
        var rule = RecurrenceRule.Create("FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=29", "America/New_York").Value;

        var preview = RecurrencePreview.Compute(rule, RecurrenceMode.FixedSchedule, Starts, null, null, 2);

        Assert.Equal(D(2028, 2, 29), preview.Dates[0]);
        Assert.Contains("February 29 only exists in leap years.", preview.Warnings);
    }

    [Theory]
    [InlineData("FREQ=DAILY", RecurrenceMode.FixedSchedule, "Every day")]
    [InlineData("FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR", RecurrenceMode.FixedSchedule, "Every weekday")]
    [InlineData("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO", RecurrenceMode.FixedSchedule, "Every 2 weeks on Monday")]
    [InlineData("FREQ=WEEKLY;BYDAY=MO,TH", RecurrenceMode.FixedSchedule, "Every week on Monday and Thursday")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=15", RecurrenceMode.FixedSchedule, "Every month on the 15th")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=-1", RecurrenceMode.FixedSchedule, "Every month on the last day")]
    [InlineData("FREQ=MONTHLY;INTERVAL=2;BYMONTHDAY=10", RecurrenceMode.FixedSchedule, "Every 2 months on the 10th")]
    [InlineData("FREQ=MONTHLY;BYDAY=2TU", RecurrenceMode.FixedSchedule, "Every month on the second Tuesday")]
    [InlineData("FREQ=MONTHLY;BYDAY=-1FR", RecurrenceMode.FixedSchedule, "Every month on the last Friday")]
    [InlineData(
        "FREQ=YEARLY;BYMONTH=1,4,7,10;BYMONTHDAY=-1",
        RecurrenceMode.FixedSchedule,
        "Every quarter on the last day of Jan, Apr, Jul and Oct"
    )]
    [InlineData(
        "FREQ=YEARLY;BYMONTH=1,4,6,9;BYMONTHDAY=15",
        RecurrenceMode.FixedSchedule,
        "Every year on the 15th of Jan, Apr, Jun and Sep"
    )]
    [InlineData("FREQ=YEARLY;BYMONTH=4;BYMONTHDAY=15", RecurrenceMode.FixedSchedule, "Every year on April 15")]
    [InlineData(
        "FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=-1",
        RecurrenceMode.FixedSchedule,
        "Every year on the last day of February"
    )]
    [InlineData(
        "FREQ=DAILY;INTERVAL=90",
        RecurrenceMode.AfterCompletion,
        "90 days after the previous one is completed"
    )]
    [InlineData(
        "FREQ=MONTHLY;INTERVAL=3",
        RecurrenceMode.AfterCompletion,
        "3 months after the previous one is completed"
    )]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=31", RecurrenceMode.FixedSchedule, "Custom schedule")]
    [InlineData("FREQ=MONTHLY;BYMONTH=1,4;BYDAY=MO;BYSETPOS=1", RecurrenceMode.FixedSchedule, "Custom schedule")]
    public void The_summary_reads_like_a_person_would_say_it(string rule, RecurrenceMode mode, string expected)
    {
        Assert.Equal(expected, RecurrenceSummary.Describe(rule, mode));
    }

    [Theory]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=31", "Months without day 31 are skipped.")]
    [InlineData("FREQ=YEARLY;BYMONTH=1,4,6,9;BYMONTHDAY=30", "Months without day 30 are skipped.")]
    public void Days_29_to_31_warn_that_short_months_are_skipped(string rule, string warning)
    {
        var preview = RecurrencePreview.Compute(
            RecurrenceRule.Create(rule, "America/New_York").Value,
            RecurrenceMode.FixedSchedule,
            Starts,
            null,
            null,
            3
        );

        Assert.Contains(warning, preview.Warnings);
    }

    [Theory]
    [InlineData("MONTHLY;BYMONTHDAY=15", "FREQ")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=abc", "can't be read")]
    public void An_unreadable_rule_says_why_and_keeps_its_code(string rule, string reason)
    {
        var error = RecurrenceRule.Create(rule, "America/New_York").Error;

        Assert.Equal(TaxVision.Tasks.Domain.Tasks.TaskErrors.Series.RuleInvalid.Code, error.Code);
        Assert.Contains(reason, error.Message);
    }
}
