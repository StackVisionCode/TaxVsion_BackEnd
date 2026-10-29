using TaxVision.Tasks.ClientForms.Domain.Versions;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Responses;

public sealed record FormTemplateVersionSummaryResponse(
    Guid Id,
    int VersionNumber,
    string Title,
    int ComponentCount,
    DateTime PublishedAtUtc,
    Guid PublishedByUserId
)
{
    public static FormTemplateVersionSummaryResponse From(FormTemplateVersion version) =>
        new(
            version.Id,
            version.VersionNumber,
            version.Title,
            version.ComponentCount,
            version.PublishedAtUtc,
            version.PublishedByUserId
        );
}
