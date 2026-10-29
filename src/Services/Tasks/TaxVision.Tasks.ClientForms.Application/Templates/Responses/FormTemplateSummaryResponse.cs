using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Responses;

public sealed record FormTemplateSummaryResponse(
    Guid Id,
    string Name,
    string? Description,
    FormPurpose Purpose,
    FormTemplateStatus Status,
    int LatestVersionNumber,
    DateTime DraftUpdatedAtUtc
)
{
    public static FormTemplateSummaryResponse From(FormTemplate template) =>
        new(
            template.Id,
            template.Name.Value,
            template.Description?.Value,
            template.Purpose,
            template.Status,
            template.LatestVersionNumber,
            template.DraftUpdatedAtUtc
        );
}
