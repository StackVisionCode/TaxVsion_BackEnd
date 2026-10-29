using TaxVision.Tasks.ClientForms.Domain.Consents;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Common.Abstractions;

/// <summary>
/// Puerto de métricas de Client Forms: un método por hecho de negocio. Los tags salen de conjuntos
/// cerrados (<see cref="ClientFormsMetricValues"/>); nunca llevan tenant, email ni nombres.
/// </summary>
public interface IClientFormsMetrics
{
    void RecordRequestAssigned();

    /// <summary>En el primer envío se mide el tiempo desde la asignación; en un reenvío no.</summary>
    void RecordSubmitted(bool resubmission, TimeSpan? sinceRequested);

    void RecordIntakeSubmitted();

    void RecordReviewed(string outcome, TimeSpan? sinceSubmitted);

    void RecordDraftSave(string outcome, TimeSpan duration);

    void RecordAttachmentVerdict(string status, string source);

    void RecordSensitiveReveal(string outcome);

    void RecordSummary(string outcome);

    void RecordProfileUpdate(string outcome);

    void RecordExternalOtp(string otpEvent);

    void RecordActionRun(string actionType, FormActionRunStatus status);

    void RecordFileReassignment(string outcome);

    void RecordConsent(FormConsentOutcome outcome);

    void RecordTemplatePublished(TimeSpan duration);

    /// <summary>X-5 — conteos de retención por regla (p. ej. "R-2") y resultado (dry_run | purged | skipped).</summary>
    void RecordRetentionPurged(string rule, string result, long count);
}
