using TaxVision.DataCollection.Engine.Validation;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests;

public sealed record FormDraftSaveOutcome(
    bool Saved,
    string? RowVersion,
    DateTime? SavedAtUtc,
    int? Progress,
    IReadOnlyList<ValidationIssue> Issues
)
{
    public static FormDraftSaveOutcome Invalid(IReadOnlyList<ValidationIssue> issues) =>
        new(false, null, null, null, issues);

    public static FormDraftSaveOutcome Success(FormRequest request, DateTime savedAtUtc) =>
        new(true, Convert.ToBase64String(request.RowVersion), savedAtUtc, request.DraftProgressPercent, []);
}

public sealed record FormSubmitOutcome(
    bool Submitted,
    FormRequestStatus? Status,
    int? RevisionNumber,
    string? RowVersion,
    IReadOnlyList<ValidationIssue> Issues
)
{
    public static FormSubmitOutcome Invalid(IReadOnlyList<ValidationIssue> issues) =>
        new(false, null, null, null, issues);

    public static FormSubmitOutcome Success(FormRequest request) =>
        new(true, request.Status, request.CurrentRevisionNumber, Convert.ToBase64String(request.RowVersion), []);
}

public sealed record CapturedSensitiveAnswerResponse(
    Guid SensitiveRef,
    string Last4,
    FormTaxIdentifierKind Kind,
    string RowVersion
);
