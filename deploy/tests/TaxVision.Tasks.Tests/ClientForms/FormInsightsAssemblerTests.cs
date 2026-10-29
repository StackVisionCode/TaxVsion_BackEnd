using TaxVision.Tasks.ClientForms.Application.Insights;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.Tests.ClientForms;

// Armado de Insights sin base: medianas desde el histograma, tasas, serie con ceros y motivos de fallo.
public sealed class FormInsightsAssemblerTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 15, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void The_median_of_an_odd_histogram_is_the_middle_value()
    {
        Assert.Equal(60, FormInsightsAssembler.Median([(30, 1), (60, 1), (600, 1)]));
        Assert.Equal(60, FormInsightsAssembler.Median([(600, 1), (60, 3), (30, 1)]));
    }

    [Fact]
    public void The_median_of_an_even_histogram_averages_the_two_middle_values()
    {
        Assert.Equal(45, FormInsightsAssembler.Median([(30, 1), (60, 1)]));
        Assert.Equal(330, FormInsightsAssembler.Median([(60, 2), (600, 2)]));
    }

    [Fact]
    public void An_empty_histogram_has_no_median()
    {
        Assert.Null(FormInsightsAssembler.Median([]));
        Assert.Null(FormInsightsAssembler.Median([(30, 0)]));
    }

    [Fact]
    public void A_rate_over_zero_is_unknown_not_zero()
    {
        Assert.Null(FormInsightsAssembler.Rate(0, 0));
        Assert.Equal(0.3333, FormInsightsAssembler.Rate(1, 3));
    }

    [Fact]
    public void Weekly_trend_folds_days_into_seven_day_buckets_from_the_first_day()
    {
        var window = FormInsightsWindow.Create(90, 0, Now);
        var first = window.FirstLocalDay.ToDateTime(TimeOnly.MinValue);
        var raw = Raw(
            sentByDay: [new(first, 2), new(first.AddDays(6), 1), new(first.AddDays(7), 4), new(first.AddDays(-1), 9)],
            completedByDay: [new(first.AddDays(89), 3)]
        );

        var trend = FormInsightsAssembler.Summary(raw, window).Trend;

        Assert.Equal(13, trend.Count);
        Assert.Equal((window.FirstLocalDay, 3, 0), (trend[0].Start, trend[0].Sent, trend[0].Completed));
        Assert.Equal((window.FirstLocalDay.AddDays(7), 4), (trend[1].Start, trend[1].Sent));
        Assert.Equal(3, trend[^1].Completed);
        Assert.Equal(7, trend.Sum(point => point.Sent));
    }

    [Fact]
    public void Failure_reasons_merge_runs_pdf_and_signature_and_keep_the_top_five()
    {
        var raw = Raw(
            failures:
            [
                new("CreateCustomer", "duplicate_found", 4),
                new("CreateCustomer", null, 1),
                new("UpdateCustomerProfile", "invalid_target", 2),
                new("CreateCustomer", "a", 1),
                new("CreateCustomer", "b", 1),
            ],
            pdfs: [new(FormRevisionPdfStatus.Failed, 3), new(FormRevisionPdfStatus.Ready, 9)],
            signatures: [new(FormRevisionSignatureStatus.Failed, 2)]
        );

        var reasons = FormInsightsAssembler.Summary(raw, FormInsightsWindow.Create(30, 0, Now)).TopFailureReasons;

        Assert.Equal(
            [
                ("CreateCustomer", "duplicate_found", 4),
                ("SummaryPdf", FormInsightsAssembler.PdfFailedCode, 3),
                ("Signature", FormInsightsAssembler.SignatureFailedCode, 2),
                ("UpdateCustomerProfile", "invalid_target", 2),
                ("CreateCustomer", "a", 1),
            ],
            reasons.Select(item => (item.Kind, item.Code, item.Count)).ToArray()
        );
    }

    [Fact]
    public void Signature_automation_succeeds_once_the_request_reaches_the_signer()
    {
        var raw = Raw(
            signatures:
            [
                new(FormRevisionSignatureStatus.Requesting, 1),
                new(FormRevisionSignatureStatus.Sent, 2),
                new(FormRevisionSignatureStatus.Declined, 1),
                new(FormRevisionSignatureStatus.Expired, 1),
                new(FormRevisionSignatureStatus.Sealed, 3),
                new(FormRevisionSignatureStatus.Failed, 1),
            ]
        );

        var summary = FormInsightsAssembler.Summary(raw, FormInsightsWindow.Create(30, 0, Now));

        Assert.Equal(
            new FormInsightsAutomationResponse("Signature", 7, 1, 0, 0, 1),
            summary.Automation.Single(item => item.Kind == "Signature")
        );
        Assert.Equal(new FormInsightsSignaturesResponse(9, 3, 3, 1, 1, 0, 1), summary.Signatures);
    }

    [Fact]
    public void The_window_covers_whole_local_days_and_the_previous_period_has_the_same_length()
    {
        var window = FormInsightsWindow.Create(7, -300, Now);

        // 15:30Z es 10:30 en UTC-5: el primer día local es el 22 y empieza a las 05:00Z.
        Assert.Equal(new DateOnly(2026, 10, 22), window.FirstLocalDay);
        Assert.Equal(new DateTime(2026, 10, 22, 5, 0, 0, DateTimeKind.Utc), window.FromUtc);
        Assert.Equal(Now, window.ToUtc);
        Assert.Equal(window.FromUtc - (window.ToUtc - window.FromUtc), window.PreviousFromUtc);
        Assert.Equal(FormInsightsGranularity.Day, window.Granularity);
    }

    [Theory]
    [InlineData(14, 30)]
    [InlineData(0, 30)]
    [InlineData(90, 90)]
    public void An_unknown_period_falls_back_to_thirty_days(int requested, int expected)
    {
        Assert.Equal(expected, FormInsightsWindow.Create(requested, 0, Now).Days);
        Assert.Equal(requested is 7 or 30 or 90, FormInsightsWindow.IsAllowed(requested));
    }

    [Fact]
    public void The_offset_is_clamped_to_fourteen_hours()
    {
        Assert.Equal(840, FormInsightsWindow.Create(30, 5000, Now).UtcOffsetMinutes);
        Assert.Equal(-840, FormInsightsWindow.Create(30, -5000, Now).UtcOffsetMinutes);
    }

    private static FormInsightsRawSummary Raw(
        IReadOnlyList<FormInsightsDayCount>? sentByDay = null,
        IReadOnlyList<FormInsightsDayCount>? completedByDay = null,
        IReadOnlyList<FormInsightsFailureTally>? failures = null,
        IReadOnlyList<FormInsightsPdfTally>? pdfs = null,
        IReadOnlyList<FormInsightsSignatureTally>? signatures = null
    ) =>
        new(
            [],
            [],
            0,
            sentByDay ?? [],
            completedByDay ?? [],
            [],
            failures ?? [],
            pdfs ?? [],
            signatures ?? [],
            [],
            true
        );
}
