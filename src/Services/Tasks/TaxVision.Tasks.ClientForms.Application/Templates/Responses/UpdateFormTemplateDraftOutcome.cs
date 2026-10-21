using TaxVision.DataCollection.Engine.Validation;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Responses;

public sealed record UpdateFormTemplateDraftOutcome(
    bool Updated,
    string? RowVersion,
    string? DraftHash,
    IReadOnlyList<ValidationIssue> Issues
)
{
    public static UpdateFormTemplateDraftOutcome Invalid(IReadOnlyList<ValidationIssue> issues) =>
        new(false, null, null, issues);

    public static UpdateFormTemplateDraftOutcome Saved(string rowVersion, string draftHash, IReadOnlyList<ValidationIssue> issues) =>
        new(true, rowVersion, draftHash, issues);
}
