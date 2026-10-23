using System.Diagnostics.Metrics;
using System.Text;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Consents;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.Infrastructure.Observability;

/// <summary>
/// Adaptador OTel de <see cref="IClientFormsMetrics"/>. El Meter <see cref="MeterName"/> se registra
/// como <c>additionalMeterNames</c>; sin eso no exporta nada. Sin tag de tenant, por cardinalidad.
/// </summary>
public sealed class ClientFormsMetrics : IClientFormsMetrics, IDisposable
{
    public const string MeterName = "TaxVision.Tasks.ClientForms";

    public string Name => _meter.Name;

    private readonly Meter _meter;
    private readonly Counter<long> _requestsAssigned;
    private readonly Counter<long> _submissions;
    private readonly Counter<long> _reviews;
    private readonly Counter<long> _draftSaves;
    private readonly Counter<long> _attachmentVerdicts;
    private readonly Counter<long> _sensitiveReveals;
    private readonly Counter<long> _summaries;
    private readonly Counter<long> _profileUpdates;
    private readonly Counter<long> _externalOtp;
    private readonly Counter<long> _intakeSubmissions;
    private readonly Counter<long> _actionRuns;
    private readonly Counter<long> _fileReassignments;
    private readonly Counter<long> _consents;
    private readonly Histogram<double> _timeToFirstSubmit;
    private readonly Histogram<double> _reviewTurnaround;
    private readonly Histogram<double> _publishDuration;
    private readonly Histogram<double> _draftSaveDuration;
    private volatile ClientFormsBacklog? _backlog;

    /// <param name="meterName">Solo los tests lo cambian, para no escuchar las mediciones de otra suite.</param>
    public ClientFormsMetrics(string? meterName = null)
    {
        _meter = new Meter(meterName ?? MeterName);
        _requestsAssigned = Counter("client_forms.requests_assigned_total", "Form requests assigned");
        _submissions = Counter("client_forms.submissions_total", "Form submissions");
        _reviews = Counter("client_forms.reviews_total", "Form reviews by outcome");
        _draftSaves = Counter("client_forms.draft_saves_total", "Draft saves by outcome");
        _attachmentVerdicts = Counter("client_forms.attachments_verdicts_total", "Attachment verdicts applied");
        _sensitiveReveals = Counter("client_forms.sensitive_reveals_total", "Sensitive answer reveals");
        _summaries = Counter("client_forms.summaries_total", "Form summaries by outcome");
        _profileUpdates = Counter("client_forms.profile_updates_total", "Customer profile updates by outcome");
        _externalOtp = Counter("client_forms.external_otp_total", "External access codes by event");
        _intakeSubmissions = Counter("client_forms.intake_submissions_total", "First submissions from intake links");
        _actionRuns = Counter("client_forms.action_runs_total", "Form action runs by type and status");
        _fileReassignments = Counter("client_forms.file_reassignments_total", "Participant files moved to customer");
        _consents = Counter("client_forms.consents_recorded_total", "Consent evidence recorded");
        _timeToFirstSubmit = Histogram("client_forms.time_to_first_submit_hours", "h", "Assignment to first submit");
        _reviewTurnaround = Histogram("client_forms.review_turnaround_hours", "h", "Submission to review");
        _publishDuration = Histogram("client_forms.publish_duration_ms", "ms", "Template publish duration");
        _draftSaveDuration = Histogram("client_forms.draft_save_duration_ms", "ms", "Draft save duration");
        CreateBacklogGauges();
    }

    public void RecordRequestAssigned() => _requestsAssigned.Add(1);

    public void RecordSubmitted(bool resubmission, TimeSpan? sinceRequested)
    {
        _submissions.Add(1, Tag("resubmission", resubmission));
        if (sinceRequested is { } elapsed)
            _timeToFirstSubmit.Record(Math.Max(0, elapsed.TotalHours));
    }

    public void RecordIntakeSubmitted() => _intakeSubmissions.Add(1);

    public void RecordReviewed(string outcome, TimeSpan? sinceSubmitted)
    {
        _reviews.Add(1, Tag("outcome", outcome));
        if (sinceSubmitted is { } elapsed)
            _reviewTurnaround.Record(Math.Max(0, elapsed.TotalHours));
    }

    public void RecordDraftSave(string outcome, TimeSpan duration)
    {
        _draftSaves.Add(1, Tag("outcome", outcome));
        _draftSaveDuration.Record(duration.TotalMilliseconds);
    }

    public void RecordAttachmentVerdict(string status, string source) =>
        _attachmentVerdicts.Add(1, Tag("status", status), Tag("source", source));

    public void RecordSensitiveReveal(string outcome) => _sensitiveReveals.Add(1, Tag("outcome", outcome));

    public void RecordSummary(string outcome) => _summaries.Add(1, Tag("outcome", outcome));

    public void RecordProfileUpdate(string outcome) => _profileUpdates.Add(1, Tag("outcome", outcome));

    public void RecordExternalOtp(string otpEvent) => _externalOtp.Add(1, Tag("event", otpEvent));

    public void RecordActionRun(string actionType, FormActionRunStatus status) =>
        _actionRuns.Add(1, Tag("type", SnakeCase(actionType)), Tag("status", SnakeCase(status.ToString())));

    // Lo que no es un resultado conocido de CloudStorage se agrupa como error: el detalle va al log.
    public void RecordFileReassignment(string outcome) =>
        _fileReassignments.Add(
            1,
            Tag(
                "outcome",
                FormFileReassignmentOutcomes.IsKnown(outcome) ? SnakeCase(outcome) : ClientFormsMetricValues.Error
            )
        );

    public void RecordConsent(FormConsentOutcome outcome) =>
        _consents.Add(1, Tag("outcome", SnakeCase(outcome.ToString())));

    public void RecordTemplatePublished(TimeSpan duration) => _publishDuration.Record(duration.TotalMilliseconds);

    public void UpdateBacklog(ClientFormsBacklog backlog) => _backlog = backlog;

    public void Dispose() => _meter.Dispose();

    // Antes de la primera lectura no se publica nada: un cero inventado taparía una alerta real.
    private void CreateBacklogGauges()
    {
        _meter.CreateObservableGauge(
            "client_forms.attachments_pending_stale",
            () => Observe(backlog => [new(backlog.AttachmentsPendingStale)]),
            description: "Attachments pending a verdict for more than 10 minutes"
        );
        _meter.CreateObservableGauge(
            "client_forms.submissions_awaiting_review",
            () =>
                Observe(backlog =>
                    [
                        new(backlog.AwaitingReviewUnderOneDay, Tag("age_bucket", "<1d")),
                        new(backlog.AwaitingReviewOneToThreeDays, Tag("age_bucket", "1-3d")),
                        new(backlog.AwaitingReviewOverThreeDays, Tag("age_bucket", ">3d")),
                    ]
                ),
            description: "Submitted requests waiting for staff review"
        );
        _meter.CreateObservableGauge(
            "client_forms.action_runs_pending_stale",
            () => Observe(backlog => [new(backlog.ActionRunsPendingStale)]),
            description: "Action runs still requested after 10 minutes"
        );
    }

    private IEnumerable<Measurement<long>> Observe(Func<ClientFormsBacklog, Measurement<long>[]> read) =>
        _backlog is { } backlog ? read(backlog) : [];

    private Counter<long> Counter(string name, string description) =>
        _meter.CreateCounter<long>(name, description: description);

    private Histogram<double> Histogram(string name, string unit, string description) =>
        _meter.CreateHistogram<double>(name, unit, description);

    private static KeyValuePair<string, object?> Tag(string key, object? value) => new(key, value);

    private static string SnakeCase(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (char.IsUpper(current) && index > 0 && value[index - 1] != '_')
                builder.Append('_');
            builder.Append(char.ToLowerInvariant(current));
        }
        return builder.ToString();
    }
}
