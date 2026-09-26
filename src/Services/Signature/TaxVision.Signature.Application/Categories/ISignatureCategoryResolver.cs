using BuildingBlocks.Results;

namespace TaxVision.Signature.Application.Categories;

/// <summary>
/// Valida una categoría entrante (sistema o custom del tenant, no archivada) y devuelve su nombre
/// canónico para congelarlo en la solicitud/plantilla. Rechaza nombres desconocidos.
/// </summary>
public interface ISignatureCategoryResolver
{
    Task<Result<string>> ResolveAsync(Guid tenantId, string category, CancellationToken ct = default);
}
