using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.ClientForms.Application.Library;

// Tarjeta de la biblioteca para los tenants. LatestVersionNumber es la última versión revisada.
public sealed record LibraryTemplateSummaryResponse(
    string Key,
    string Name,
    string? Description,
    FormPurpose Purpose,
    int LatestVersionNumber
);

// Preview: la definición de la última versión revisada, en solo lectura.
public sealed record LibraryTemplatePreviewResponse(
    string Key,
    string Name,
    string? Description,
    FormPurpose Purpose,
    int VersionNumber,
    string Definition
);
