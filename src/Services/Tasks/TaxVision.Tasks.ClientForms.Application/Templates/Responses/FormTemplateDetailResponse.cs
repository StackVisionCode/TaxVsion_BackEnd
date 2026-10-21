using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Responses;

public sealed record FormTemplateDetailResponse(
    Guid Id,
    string Name,
    string? Description,
    FormPurpose Purpose,
    FormTemplateStatus Status,
    string Draft,
    string DraftHash,
    string RowVersion,
    int LatestVersionNumber,
    Guid? LatestVersionId,
    DateTime DraftUpdatedAtUtc,
    Guid DraftUpdatedByUserId,
    IReadOnlyList<FormTemplateVersionSummaryResponse> Versions
)
{
    public static FormTemplateDetailResponse From(
        FormTemplate template,
        IReadOnlyList<FormTemplateVersionSummaryResponse>? versions = null
    ) =>
        new(
            template.Id,
            template.Name.Value,
            template.Description?.Value,
            template.Purpose,
            template.Status,
            template.Draft.Json,
            template.DraftHash.Value,
            Convert.ToBase64String(template.RowVersion),
            template.LatestVersionNumber,
            template.LatestVersionId,
            template.DraftUpdatedAtUtc,
            template.DraftUpdatedByUserId,
            versions ?? []
        );
}
