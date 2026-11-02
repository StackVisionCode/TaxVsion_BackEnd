using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Application.Limits;
using TaxVision.Signature.Application.Platform;
using TaxVision.Signature.Domain.Platform;
using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Infrastructure.Persistence.Repositories;

// tenantId explícito: IgnoreQueryFilters() porque el filtro ambiental puede no estar poblado (consumers).
public sealed class TenantSignatureLimitsRepository(SignatureDbContext db) : ITenantSignatureLimitsRepository
{
    public Task<TenantSignatureLimits?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        db.TenantSignatureLimits.IgnoreQueryFilters().FirstOrDefaultAsync(limits => limits.TenantId == tenantId, ct);

    public void Add(TenantSignatureLimits limits) => db.TenantSignatureLimits.Add(limits);
}

public sealed class SignaturePlatformSettingsRepository(SignatureDbContext db) : ISignaturePlatformSettingsRepository
{
    public Task<SignaturePlatformSettings?> GetAsync(CancellationToken ct) =>
        db.SignaturePlatformSettings.AsNoTracking()
            .SingleOrDefaultAsync(settings => settings.Id == SignaturePlatformSettings.SingletonId, ct);

    public Task<SignaturePlatformSettings?> GetForUpdateAsync(CancellationToken ct) =>
        db.SignaturePlatformSettings.SingleOrDefaultAsync(settings => settings.Id == SignaturePlatformSettings.SingletonId, ct);
}
