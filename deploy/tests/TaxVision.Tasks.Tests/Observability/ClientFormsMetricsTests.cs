using System.Diagnostics.Metrics;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Domain.Consents;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Infrastructure.Observability;

namespace TaxVision.Tasks.Tests.Observability;

/// <summary>
/// Cada test estrena su propio nombre de Meter: escuchar el de producción mezclaría las mediciones
/// de otras suites que corren a la vez.
/// </summary>
public sealed class ClientFormsMetricsTests
{
    [Fact]
    public void The_production_meter_name_is_the_one_registered_with_open_telemetry()
    {
        using var metrics = new ClientFormsMetrics();

        Assert.Equal("TaxVision.Tasks.ClientForms", metrics.Name);
    }

    [Fact]
    public void Only_the_first_submit_records_the_time_since_assignment()
    {
        using var metrics = NewMetrics();
        var submissions = new List<object?>();
        var hours = new List<double>();
        using var counter = Listen<long>(
            metrics,
            "client_forms.submissions_total",
            (_, tags) => submissions.Add(tags["resubmission"])
        );
        using var histogram = Listen<double>(
            metrics,
            "client_forms.time_to_first_submit_hours",
            (value, _) => hours.Add(value)
        );

        metrics.RecordSubmitted(false, TimeSpan.FromHours(5));
        metrics.RecordSubmitted(true, null);

        Assert.Equal([false, true], submissions);
        Assert.Equal([5d], hours);
    }

    [Fact]
    public void Reviews_carry_the_outcome_and_the_turnaround()
    {
        using var metrics = NewMetrics();
        var outcomes = new List<object?>();
        var hours = new List<double>();
        using var counter = Listen<long>(
            metrics,
            "client_forms.reviews_total",
            (_, tags) => outcomes.Add(tags["outcome"])
        );
        using var histogram = Listen<double>(
            metrics,
            "client_forms.review_turnaround_hours",
            (value, _) => hours.Add(value)
        );

        metrics.RecordReviewed(ClientFormsMetricValues.Returned, TimeSpan.FromMinutes(90));
        metrics.RecordReviewed(ClientFormsMetricValues.Completed, null);

        Assert.Equal(["returned", "completed"], outcomes);
        Assert.Equal([1.5], hours);
    }

    [Fact]
    public void Action_runs_and_consents_use_snake_case_tags()
    {
        using var metrics = NewMetrics();
        var runs = new List<(object? Type, object? Status)>();
        var consents = new List<object?>();
        using var runListener = Listen<long>(
            metrics,
            "client_forms.action_runs_total",
            (_, tags) => runs.Add((tags["type"], tags["status"]))
        );
        using var consentListener = Listen<long>(
            metrics,
            "client_forms.consents_recorded_total",
            (_, tags) => consents.Add(tags["outcome"])
        );

        metrics.RecordActionRun("CreateCustomer", FormActionRunStatus.Requested);
        metrics.RecordConsent(FormConsentOutcome.Declined);

        Assert.Equal([("create_customer", "requested")], runs);
        Assert.Equal(["declined"], consents);
    }

    [Theory]
    [InlineData(FormFileReassignmentOutcomes.Reassigned, "reassigned")]
    [InlineData(FormFileReassignmentOutcomes.AlreadyApplied, "already_applied")]
    [InlineData(FormFileReassignmentOutcomes.OwnerMismatch, "owner_mismatch")]
    [InlineData(FormFileReassignmentOutcomes.LegalHold, "legal_hold")]
    [InlineData(FormFileReassignmentOutcomes.NotFound, "not_found")]
    [InlineData("http_503", "error")]
    public void File_reassignments_keep_a_closed_set_of_outcomes(string outcome, string expected)
    {
        using var metrics = NewMetrics();
        var recorded = new List<object?>();
        using var listener = Listen<long>(
            metrics,
            "client_forms.file_reassignments_total",
            (_, tags) => recorded.Add(tags["outcome"])
        );

        metrics.RecordFileReassignment(outcome);

        Assert.Equal([expected], recorded);
    }

    [Fact]
    public void Attachment_verdicts_carry_status_and_source()
    {
        using var metrics = NewMetrics();
        var recorded = new List<(object? Status, object? Source)>();
        using var listener = Listen<long>(
            metrics,
            "client_forms.attachments_verdicts_total",
            (_, tags) => recorded.Add((tags["status"], tags["source"]))
        );

        metrics.RecordAttachmentVerdict(
            ClientFormsMetricValues.Of(FormFileVerdictStatus.ScanFailed),
            ClientFormsMetricValues.SourceEvent
        );

        Assert.Equal([("scan_failed", "event")], recorded);
    }

    [Fact]
    public void Durations_are_recorded_in_milliseconds()
    {
        using var metrics = NewMetrics();
        var publish = new List<double>();
        var drafts = new List<double>();
        using var publishListener = Listen<double>(
            metrics,
            "client_forms.publish_duration_ms",
            (value, _) => publish.Add(value)
        );
        using var draftListener = Listen<double>(
            metrics,
            "client_forms.draft_save_duration_ms",
            (value, _) => drafts.Add(value)
        );

        metrics.RecordTemplatePublished(TimeSpan.FromSeconds(2));
        metrics.RecordDraftSave(ClientFormsMetricValues.Ok, TimeSpan.FromMilliseconds(40));

        Assert.Equal([2000d], publish);
        Assert.Equal([40d], drafts);
    }

    // Antes de la primera lectura del job no se publica nada: un cero inventado taparía una alerta.
    [Fact]
    public void Backlog_gauges_report_nothing_until_the_first_snapshot()
    {
        using var metrics = NewMetrics();
        var observed = new List<(string Instrument, long Value, object? Bucket)>();
        using var listener = Listen<long>(
            metrics,
            null,
            (value, tags, instrument) => observed.Add((instrument, value, tags.GetValueOrDefault("age_bucket")))
        );

        listener.RecordObservableInstruments();
        Assert.Empty(observed);

        metrics.UpdateBacklog(new ClientFormsBacklog(2, 5, 3, 1, 4));
        listener.RecordObservableInstruments();

        Assert.Equal(
            [
                ("client_forms.attachments_pending_stale", 2L, null),
                ("client_forms.submissions_awaiting_review", 5L, "<1d"),
                ("client_forms.submissions_awaiting_review", 3L, "1-3d"),
                ("client_forms.submissions_awaiting_review", 1L, ">3d"),
                ("client_forms.action_runs_pending_stale", 4L, null),
            ],
            observed
        );
    }

    [Fact]
    public void No_instrument_carries_a_tenant_tag()
    {
        using var metrics = NewMetrics();
        var keys = new HashSet<string>();
        using var listener = Listen<long>(metrics, null, (_, tags, _) => keys.UnionWith(tags.Keys));

        metrics.RecordRequestAssigned();
        metrics.RecordIntakeSubmitted();
        metrics.RecordSensitiveReveal(ClientFormsMetricValues.Granted);
        metrics.RecordExternalOtp(ClientFormsMetricValues.OtpLocked);
        metrics.RecordSummary(ClientFormsMetricValues.Ok);
        metrics.RecordProfileUpdate(ClientFormsMetricValues.Error);
        metrics.RecordDraftSave(ClientFormsMetricValues.Conflict, TimeSpan.Zero);

        Assert.Equal(["event", "outcome"], keys.Order(StringComparer.Ordinal));
    }

    private static ClientFormsMetrics NewMetrics() => new($"{ClientFormsMetrics.MeterName}.{Guid.NewGuid():N}");

    private static MeterListener Listen<T>(
        ClientFormsMetrics metrics,
        string instrument,
        Action<T, IReadOnlyDictionary<string, object?>> onMeasured
    )
        where T : struct => Listen<T>(metrics, instrument, (value, tags, _) => onMeasured(value, tags));

    // instrument null escucha todo el Meter del test.
    private static MeterListener Listen<T>(
        ClientFormsMetrics metrics,
        string? instrument,
        Action<T, IReadOnlyDictionary<string, object?>, string> onMeasured
    )
        where T : struct
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (i, l) =>
            {
                if (i.Meter.Name == metrics.Name && (instrument is null || i.Name == instrument))
                    l.EnableMeasurementEvents(i);
            },
        };

        listener.SetMeasurementEventCallback<T>(
            (i, value, tags, _) =>
                onMeasured(value, tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value), i.Name)
        );
        listener.Start();
        return listener;
    }
}
