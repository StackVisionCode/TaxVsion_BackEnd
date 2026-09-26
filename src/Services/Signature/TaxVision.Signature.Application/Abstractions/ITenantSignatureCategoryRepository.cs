using TaxVision.Signature.Domain.Categories;

namespace TaxVision.Signature.Application.Abstractions;

/// <summary>
/// Repositorio de las categorías custom del tenant (14.5). Como todo el read-path de Signature,
/// filtra por <c>TenantId</c> explícito.
/// </summary>
public interface ITenantSignatureCategoryRepository
{
    Task<TenantSignatureCategory?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<TenantSignatureCategory>> ListAsync(
        Guid tenantId,
        bool includeArchived,
        CancellationToken ct = default
    );

    /// <summary>¿Ya existe una categoría con ese nombre normalizado? <paramref name="excludingId"/> excluye la que se renombra.</summary>
    Task<bool> ExistsByNormalizedNameAsync(
        Guid tenantId,
        string normalizedName,
        Guid? excludingId,
        CancellationToken ct = default
    );

    Task AddAsync(TenantSignatureCategory category, CancellationToken ct = default);
}
