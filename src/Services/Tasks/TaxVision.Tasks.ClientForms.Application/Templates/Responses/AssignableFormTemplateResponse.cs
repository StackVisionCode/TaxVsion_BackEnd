using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Responses;

public sealed record AssignableFormTemplateResponse(
    Guid TemplateId,
    string Name,
    FormPurpose Purpose,
    int LatestVersionNumber,
    string Title
);
