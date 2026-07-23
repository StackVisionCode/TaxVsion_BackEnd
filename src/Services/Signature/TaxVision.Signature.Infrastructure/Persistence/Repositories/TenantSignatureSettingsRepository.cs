using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Infrastructure.Persistence.Repositories;

public sealed class TenantSignatureSettingsRepository(SignatureDbContext db) : ITenantSignatureSettingsRepository
{
    // Mismo bug de scope de Wolverine (ver LocalCommandTenantMiddleware.cs): tenantId ya viene
    // explícito y validado — IgnoreQueryFilters() porque el filtro ambiental global puede no
    // estar poblado en este scope de DI.
    public Task<TenantSignatureSettings?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        db
            .TenantSignatureSettings.IgnoreQueryFilters()
            .FirstOrDefaultAsync(settings => settings.TenantId == tenantId, ct);

    public async Task AddAsync(TenantSignatureSettings settings, CancellationToken ct = default) =>
        await db.TenantSignatureSettings.AddAsync(settings, ct);
}
