using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Categories;

namespace TaxVision.Signature.Application.Categories;

public sealed class SignatureCategoryResolver(ITenantSignatureCategoryRepository repository)
    : ISignatureCategoryResolver
{
    public async Task<Result<string>> ResolveAsync(Guid tenantId, string category, CancellationToken ct = default)
    {
        var trimmed = (category ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return Result.Failure<string>(new Error("Signature.Category.Required", "Category is required."));

        var normalized = TenantSignatureCategory.Normalize(trimmed);

        // Categoría de sistema: se guarda con su nombre canónico (para que consent/analytics la reconozcan).
        var system = SignatureCategoryDefaults.Names.FirstOrDefault(n =>
            TenantSignatureCategory.Normalize(n) == normalized
        );
        if (system is not null)
            return Result.Success(system);

        // Categoría custom del tenant (no archivada): se guarda con su nombre tal como lo definió el tenant.
        var custom = (await repository.ListAsync(tenantId, includeArchived: false, ct)).FirstOrDefault(c =>
            c.NormalizedName == normalized
        );
        if (custom is not null)
            return Result.Success(custom.Name);

        return Result.Failure<string>(
            new Error("Signature.Category.Unknown", "That category does not exist for this tenant.")
        );
    }
}
