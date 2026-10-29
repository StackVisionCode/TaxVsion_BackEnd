using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Consents;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.Tests.ClientForms;

/// <summary>Anota cada medición como texto: los tests miran qué se midió, no cómo se exporta.</summary>
internal sealed class RecordingClientFormsMetrics : IClientFormsMetrics
{
    public List<string> Recorded { get; } = [];

    public void RecordRequestAssigned() => Recorded.Add("assigned");

    public void RecordSubmitted(bool resubmission, TimeSpan? sinceRequested) =>
        Recorded.Add($"submitted:{resubmission}:{sinceRequested is not null}");

    public void RecordIntakeSubmitted() => Recorded.Add("intake_submitted");

    public void RecordReviewed(string outcome, TimeSpan? sinceSubmitted) =>
        Recorded.Add($"reviewed:{outcome}:{sinceSubmitted is not null}");

    public void RecordDraftSave(string outcome, TimeSpan duration) => Recorded.Add($"draft:{outcome}");

    public void RecordAttachmentVerdict(string status, string source) => Recorded.Add($"verdict:{status}:{source}");

    public void RecordSensitiveReveal(string outcome) => Recorded.Add($"reveal:{outcome}");

    public void RecordSummary(string outcome) => Recorded.Add($"summary:{outcome}");

    public void RecordProfileUpdate(string outcome) => Recorded.Add($"profile:{outcome}");

    public void RecordExternalOtp(string otpEvent) => Recorded.Add($"otp:{otpEvent}");

    public void RecordActionRun(string actionType, FormActionRunStatus status) =>
        Recorded.Add($"action:{actionType}:{status}");

    public void RecordFileReassignment(string outcome) => Recorded.Add($"reassignment:{outcome}");

    public void RecordConsent(FormConsentOutcome outcome) => Recorded.Add($"consent:{outcome}");

    public void RecordTemplatePublished(TimeSpan duration) => Recorded.Add("published");

    public void RecordRetentionPurged(string rule, string result, long count) =>
        Recorded.Add($"retention:{rule}:{result}:{count}");
}
