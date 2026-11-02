using System.Text.Json;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Quotas;
using TaxVision.CloudStorage.Domain.FileTypes;
using TaxVision.CloudStorage.Domain.Platform;

namespace TaxVision.CloudStorage.Application.Platform;

public interface IStoragePlatformSettingsRepository
{
    /// <summary>Sin seguimiento: para leer los valores vigentes.</summary>
    Task<StoragePlatformSettings?> GetAsync(CancellationToken ct);

    /// <summary>Con seguimiento: para que PlatformAdmin los cambie.</summary>
    Task<StoragePlatformSettings?> GetForUpdateAsync(CancellationToken ct);
}

/// <summary>Valores operativos vigentes (fila única en BD, caché por réplica de 60 s).</summary>
public interface IStoragePlatformSettingsProvider
{
    Task<StoragePlatformSettings> GetAsync(CancellationToken ct);

    void Invalidate();
}

public sealed class StoragePlatformSettingsProvider(
    IStoragePlatformSettingsRepository repository,
    StoragePlatformSettingsCache cache,
    ISystemClock clock
) : IStoragePlatformSettingsProvider
{
    public async Task<StoragePlatformSettings> GetAsync(CancellationToken ct)
    {
        if (cache.TryGet(clock.UtcNow, out var cached))
            return cached;

        // Sin fila (entorno sin migrar) se usan los mismos valores que siembra la migración.
        var settings = await repository.GetAsync(ct) ?? StoragePlatformSettings.Defaults();
        cache.Set(settings, clock.UtcNow);
        return settings;
    }

    public void Invalidate() => cache.Invalidate();
}

public sealed class StoragePlatformSettingsCache
{
    public static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(60);

    private (StoragePlatformSettings Settings, DateTime ExpiresAtUtc)? _entry;

    public bool TryGet(DateTime nowUtc, out StoragePlatformSettings settings)
    {
        if (_entry is { } entry && entry.ExpiresAtUtc > nowUtc)
        {
            settings = entry.Settings;
            return true;
        }
        settings = default!;
        return false;
    }

    public void Set(StoragePlatformSettings settings, DateTime nowUtc) => _entry = (settings, nowUtc + TimeToLive);

    public void Invalidate() => _entry = null;
}

// ------------------------------------------------------------------
// PlatformAdmin: ver y cambiar
// ------------------------------------------------------------------

public sealed record GetStoragePlatformSettingsQuery;

public sealed record UpdateStoragePlatformSettingsCommand(
    int SignedUrlMinutes,
    int ShareUrlMinutes,
    int UploadReservationHours,
    int RecycleBinRetentionDays,
    int MaxZipFiles,
    int MaxZipFolders,
    long MaxZipAggregateBytes,
    int SharePasswordLockMinutes,
    Guid ActorUserId
);

public sealed record StoragePlatformSettingsResponse(
    int SignedUrlMinutes,
    int ShareUrlMinutes,
    int UploadReservationHours,
    int RecycleBinRetentionDays,
    int MaxZipFiles,
    int MaxZipFolders,
    long MaxZipAggregateBytes,
    int SharePasswordLockMinutes,
    DateTime UpdatedAtUtc
)
{
    public static StoragePlatformSettingsResponse From(StoragePlatformSettings settings) =>
        new(
            settings.SignedUrlMinutes,
            settings.ShareUrlMinutes,
            settings.UploadReservationHours,
            settings.RecycleBinRetentionDays,
            settings.MaxZipFiles,
            settings.MaxZipFolders,
            settings.MaxZipAggregateBytes,
            settings.SharePasswordLockMinutes,
            settings.UpdatedAtUtc
        );
}

public static class StoragePlatformSettingsHandler
{
    private const string AuditKind = "platform_settings";

    public static async Task<StoragePlatformSettingsResponse> Handle(
        GetStoragePlatformSettingsQuery query,
        IStoragePlatformSettingsProvider provider,
        CancellationToken ct
    ) => StoragePlatformSettingsResponse.From(await provider.GetAsync(ct));

    public static async Task<Result<StoragePlatformSettingsResponse>> Handle(
        UpdateStoragePlatformSettingsCommand command,
        IStoragePlatformSettingsRepository repository,
        IStoragePlatformSettingsProvider provider,
        IFilePolicyCatalogRepository audits,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var settings = await repository.GetForUpdateAsync(ct);
        if (settings is null)
            return Result.Failure<StoragePlatformSettingsResponse>(
                new Error("StoragePlatformSettings.Missing", "Platform settings are not provisioned; apply the migrations.")
            );

        var before = JsonSerializer.Serialize(StoragePlatformSettingsResponse.From(settings));
        var updated = settings.Update(
            command.SignedUrlMinutes,
            command.ShareUrlMinutes,
            command.UploadReservationHours,
            command.RecycleBinRetentionDays,
            command.MaxZipFiles,
            command.MaxZipFolders,
            command.MaxZipAggregateBytes,
            command.SharePasswordLockMinutes,
            command.ActorUserId,
            clock.UtcNow
        );
        if (updated.IsFailure)
            return Result.Failure<StoragePlatformSettingsResponse>(updated.Error);

        var response = StoragePlatformSettingsResponse.From(settings);
        audits.AddAudit(FilePolicyAudit.Record(AuditKind, "storage", before, JsonSerializer.Serialize(response), command.ActorUserId, clock.UtcNow));
        await unitOfWork.SaveChangesAsync(ct);
        provider.Invalidate();
        return Result.Success(response);
    }
}
