using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Infrastructure.Persistence.Repositories;

public sealed class TenantSignatureSettingsRepository(SignatureDbContext db) : ITenantSignatureSettingsRepository
{
    public Task<TenantSignatureSettings?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        db.TenantSignatureSettings.FirstOrDefaultAsync(settings => settings.TenantId == tenantId, ct);

    public async Task AddAsync(TenantSignatureSettings settings, CancellationToken ct = default) =>
        await db.TenantSignatureSettings.AddAsync(settings, ct);
}
