using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.Versions;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Responses;

public sealed record FormTemplateVersionResponse(
    Guid Id,
    Guid TemplateId,
    int VersionNumber,
    int SchemaVersion,
    string Title,
    FormPurpose Purpose,
    FormPresentation Presentation,
    string Definition,
    string ContentHash,
    int ComponentCount,
    DateTime PublishedAtUtc,
    Guid PublishedByUserId
)
{
    public static FormTemplateVersionResponse From(FormTemplateVersion version) =>
        new(
            version.Id,
            version.FormTemplateId,
            version.VersionNumber,
            version.SchemaVersion,
            version.Title,
            version.Purpose,
            version.Presentation,
            version.Definition.Json,
            version.ContentHash.Value,
            version.ComponentCount,
            version.PublishedAtUtc,
            version.PublishedByUserId
        );
}
