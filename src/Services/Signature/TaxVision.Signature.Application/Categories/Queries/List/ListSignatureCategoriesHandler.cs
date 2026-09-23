using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Categories;

namespace TaxVision.Signature.Application.Categories.Queries.List;

/// <summary>Devuelve las categorías de sistema + las custom del tenant (efectivo = system ∪ custom).</summary>
public static class ListSignatureCategoriesHandler
{
    public static async Task<ListSignatureCategoriesResult> Handle(
        ListSignatureCategoriesQuery query,
        ITenantSignatureCategoryRepository repository,
        CancellationToken ct
    )
    {
        var system = SignatureCategoryDefaults.Names.Select(name => new SignatureCategoryResponse(
            Id: null,
            Name: name,
            IsSystem: true,
            IsArchived: false
        ));

        var custom = (await repository.ListAsync(query.TenantId, query.IncludeArchived, ct)).Select(
            category => new SignatureCategoryResponse(category.Id, category.Name, IsSystem: false, category.IsArchived)
        );

        return new ListSignatureCategoriesResult(system.Concat(custom).ToList());
    }
}
