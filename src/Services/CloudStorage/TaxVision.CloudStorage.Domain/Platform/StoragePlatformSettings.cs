using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.CloudStorage.Domain.Platform;

/// <summary>
/// Valores operativos de CloudStorage que administra PlatformAdmin (fila única, sin tenant): vigencia de las
/// URLs firmadas, reserva de subida, papelera, topes de ZIP y bloqueo de enlaces por contraseña. Antes vivían
/// en appsettings y cambiarlos exigía redeploy.
/// </summary>
public sealed class StoragePlatformSettings : BaseEntity
{
    public static readonly Guid SingletonId = new("3f6d1d62-4c1b-4c84-9a1e-0b6c5f1a7e21");

    /// <summary>
    /// El bucket temporal borra objetos al día (lifecycle de MinIO): una reserva más larga dejaría registros
    /// esperando un archivo que ya no existe.
    /// </summary>
    public const int MaxUploadReservationHours = 24;

    public const long MaxZipAggregateCeilingBytes = 5L * 1024 * 1024 * 1024;

    private StoragePlatformSettings() { }

    public int SignedUrlMinutes { get; private set; }
    public int ShareUrlMinutes { get; private set; }
    public int UploadReservationHours { get; private set; }
    public int RecycleBinRetentionDays { get; private set; }
    public int MaxZipFiles { get; private set; }
    public int MaxZipFolders { get; private set; }
    public long MaxZipAggregateBytes { get; private set; }
    public int SharePasswordLockMinutes { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid UpdatedByUserId { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Los valores que tenían appsettings y el código al pasar a BD (semilla y respaldo si falta la fila).</summary>
    public static StoragePlatformSettings Defaults() =>
        new()
        {
            Id = SingletonId,
            SignedUrlMinutes = 5,
            ShareUrlMinutes = 2,
            UploadReservationHours = 24,
            RecycleBinRetentionDays = 30,
            MaxZipFiles = 500,
            MaxZipFolders = 50,
            MaxZipAggregateBytes = 2L * 1024 * 1024 * 1024,
            SharePasswordLockMinutes = 15,
            UpdatedByUserId = Guid.Empty,
            UpdatedAtUtc = DateTime.UnixEpoch,
        };

    public Result Update(
        int signedUrlMinutes,
        int shareUrlMinutes,
        int uploadReservationHours,
        int recycleBinRetentionDays,
        int maxZipFiles,
        int maxZipFolders,
        long maxZipAggregateBytes,
        int sharePasswordLockMinutes,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (
            signedUrlMinutes is < 1 or > 60
            || shareUrlMinutes is < 1 or > 60
            || uploadReservationHours is < 1 or > MaxUploadReservationHours
            || recycleBinRetentionDays is < 1 or > 365
            || maxZipFiles is < 1 or > 5000
            || maxZipFolders is < 1 or > 500
            || maxZipAggregateBytes is < 1024 * 1024 or > MaxZipAggregateCeilingBytes
            || sharePasswordLockMinutes is < 1 or > 1440
        )
            return Result.Failure(StoragePlatformSettingsErrors.OutOfRange);

        SignedUrlMinutes = signedUrlMinutes;
        ShareUrlMinutes = shareUrlMinutes;
        UploadReservationHours = uploadReservationHours;
        RecycleBinRetentionDays = recycleBinRetentionDays;
        MaxZipFiles = maxZipFiles;
        MaxZipFolders = maxZipFolders;
        MaxZipAggregateBytes = maxZipAggregateBytes;
        SharePasswordLockMinutes = sharePasswordLockMinutes;
        UpdatedByUserId = actorUserId;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }
}

public static class StoragePlatformSettingsErrors
{
    public static readonly Error OutOfRange = new(
        "StoragePlatformSettings.OutOfRange",
        "URLs 1–60 min, upload reservation 1–24 h, recycle bin 1–365 days, ZIP up to 5000 files / 500 folders / 5 GB, password lock 1–1440 min."
    );
}
