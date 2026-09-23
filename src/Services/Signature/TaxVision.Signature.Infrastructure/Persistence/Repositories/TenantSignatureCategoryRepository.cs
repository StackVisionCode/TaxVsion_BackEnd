using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Categories;

namespace TaxVision.Signature.Infrastructure.Persistence.Repositories;

// tenantId explícito + IgnoreQueryFilters(): mismo criterio que el resto del read-path de Signature
// (el filtro global puede no estar poblado en el scope del handler de Wolverine).
public sealed class TenantSignatureCategoryRepository(SignatureDbContext db) : ITenantSignatureCategoryRepository
{
    public Task<TenantSignatureCategory?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        db.SignatureCategories.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, ct);

    public async Task<IReadOnlyList<TenantSignatureCategory>> ListAsync(
        Guid tenantId,
        bool includeArchived,
        CancellationToken ct = default
    ) =>
        await db
            .SignatureCategories.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && (includeArchived || !c.IsArchived))
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

    public Task<bool> ExistsByNormalizedNameAsync(
        Guid tenantId,
        string normalizedName,
        Guid? excludingId,
        CancellationToken ct = default
    ) =>
        db
            .SignatureCategories.IgnoreQueryFilters()
            .AnyAsync(
                c =>
                    c.TenantId == tenantId
                    && c.NormalizedName == normalizedName
                    && (excludingId == null || c.Id != excludingId),
                ct
            );

    public async Task AddAsync(TenantSignatureCategory category, CancellationToken ct = default) =>
        await db.SignatureCategories.AddAsync(category, ct);
}
