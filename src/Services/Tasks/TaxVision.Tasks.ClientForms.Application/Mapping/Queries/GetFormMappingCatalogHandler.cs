using TaxVision.DataCollection.Engine.Mapping;
using TaxVision.Tasks.ClientForms.Application.Mapping.Responses;

namespace TaxVision.Tasks.ClientForms.Application.Mapping.Queries;

public sealed record GetFormMappingCatalogQuery(PublisherCapabilities Capabilities);

public static class GetFormMappingCatalogHandler
{
    // Única fuente del catálogo para el builder: sin retirados y sin lo que el usuario no puede mapear.
    public static FormMappingCatalogResponse Handle(
        GetFormMappingCatalogQuery query,
        IDataCollectionTargetCatalog targetCatalog
    )
    {
        var catalog = targetCatalog.GetSnapshot();
        var targets = catalog
            .Targets.Where(target =>
                target.Status != MappingTargetStatus.Retired
                && query.Capabilities.Allows(target.RequiredPermissions, target.RequiresTenantAdmin)
            )
            .Select(FormMappingTargetResponse.From)
            .ToArray();
        var operations = catalog
            .Operations.Where(operation =>
                query.Capabilities.Allows(operation.RequiredPermissions, operation.RequiresTenantAdmin)
            )
            .Select(FormMappingOperationResponse.From)
            .ToArray();
        return new FormMappingCatalogResponse(catalog.CatalogVersion, targets, operations);
    }
}
