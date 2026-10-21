using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.Api.Requests.ClientForms;

public sealed record CreateFormTemplateRequest(
    string? Name,
    string? Description,
    FormPurpose Purpose,
    FormPresentation Presentation
);
