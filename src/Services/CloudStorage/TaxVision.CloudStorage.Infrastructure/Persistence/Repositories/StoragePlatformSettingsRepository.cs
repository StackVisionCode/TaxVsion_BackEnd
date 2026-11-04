using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Application.Platform;
using TaxVision.CloudStorage.Domain.Platform;

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;

public sealed class StoragePlatformSettingsRepository(CloudStorageDbContext db) : IStoragePlatformSettingsRepository
{
    public Task<StoragePlatformSettings?> GetAsync(CancellationToken ct) =>
        db
            .StoragePlatformSettings.AsNoTracking()
            .SingleOrDefaultAsync(settings => settings.Id == StoragePlatformSettings.SingletonId, ct);

    public Task<StoragePlatformSettings?> GetForUpdateAsync(CancellationToken ct) =>
        db.StoragePlatformSettings.SingleOrDefaultAsync(
            settings => settings.Id == StoragePlatformSettings.SingletonId,
            ct
        );
}
