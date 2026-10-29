using TaxVision.DataCollection.Engine.Validation;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Responses;

public sealed record PublishFormTemplateOutcome(
    bool Published,
    FormTemplateVersionResponse? Version,
    IReadOnlyList<ValidationIssue> Issues
)
{
    public static PublishFormTemplateOutcome Invalid(IReadOnlyList<ValidationIssue> issues) => new(false, null, issues);

    public static PublishFormTemplateOutcome Success(FormTemplateVersionResponse version) => new(true, version, []);
}
