namespace TaxVision.Tasks.ClientForms.Application.Mapping.Responses;

public sealed record FormCatalogUsageResponse(
    string TargetKey,
    int OpenRequests,
    int ActiveIntakeLinks,
    int PublishedVersions
);
