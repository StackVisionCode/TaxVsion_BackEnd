namespace TaxVision.Tasks.ClientForms.Application.Mapping.Responses;

public sealed record FormMappingCatalogResponse(
    int CatalogVersion,
    IReadOnlyList<FormMappingTargetResponse> Targets,
    IReadOnlyList<FormMappingOperationResponse> Operations
);
