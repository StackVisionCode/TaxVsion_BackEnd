using Microsoft.EntityFrameworkCore;
using TaxVision.Tenant.Application.Platform.Abstractions;
using TaxVision.Tenant.Domain.Platform;

namespace TaxVision.Tenant.Infrastructure.Persistence.Repositories;

public sealed class PlatformSettingsRepository(TenantDbContext db) : IPlatformSettingsRepository
{
    public Task<PlatformSettings?> GetAsync(CancellationToken ct = default) =>
        db.PlatformSettings.FirstOrDefaultAsync(x => x.Id == PlatformSettings.SingletonId, ct);

    public async Task AddAsync(PlatformSettings settings, CancellationToken ct = default) =>
        await db.PlatformSettings.AddAsync(settings, ct);
}
