using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Profiles;

namespace TaxVision.Signature.Infrastructure.Persistence.Repositories;

// tenantId explícito + IgnoreQueryFilters(): mismo criterio que el resto del read-path de Signature.
public sealed class SignatureProfileRepository(SignatureDbContext db) : ISignatureProfileRepository
{
    public Task<SignatureProfile?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        db.SignatureProfiles.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId, ct);

    public async Task<IReadOnlyList<SignatureProfile>> ListVisibleAsync(
        Guid tenantId,
        Guid userId,
        bool includePersonal,
        bool includeArchived,
        CancellationToken ct = default
    ) =>
        await db
            .SignatureProfiles.IgnoreQueryFilters()
            .Where(p =>
                p.TenantId == tenantId
                && (p.OwnerUserId == null || (includePersonal && p.OwnerUserId == userId))
                && (includeArchived || !p.IsArchived)
            )
            // Oficina primero, luego por etiqueta: orden estable para el picker.
            .OrderBy(p => p.OwnerUserId != null)
            .ThenBy(p => p.Label)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SignatureProfile>> ListByOwnerAsync(
        Guid tenantId,
        Guid? ownerUserId,
        bool includeArchived,
        CancellationToken ct = default
    ) =>
        await db
            .SignatureProfiles.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && p.OwnerUserId == ownerUserId && (includeArchived || !p.IsArchived))
            .ToListAsync(ct);

    public Task<SignatureProfile?> GetDefaultAsync(Guid tenantId, Guid? ownerUserId, CancellationToken ct = default) =>
        db
            .SignatureProfiles.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                p => p.TenantId == tenantId && p.OwnerUserId == ownerUserId && p.IsDefault && !p.IsArchived,
                ct
            );

    public async Task AddAsync(SignatureProfile profile, CancellationToken ct = default) =>
        await db.SignatureProfiles.AddAsync(profile, ct);

    public void Remove(SignatureProfile profile) => db.SignatureProfiles.Remove(profile);
}
