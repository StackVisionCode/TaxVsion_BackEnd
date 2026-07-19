namespace TaxVision.Scribe.Domain.Projections;

/// <summary>
/// Assets propios de la plataforma (hoy solo el logo de header) — reemplaza a la config estática
/// Scribe:SystemAssets: <see cref="ScribeSystemAssetSeeder"/> sube el archivo local a CloudStorage
/// al arrancar y persiste el FileId acá, en vez de requerir que alguien copie un GUID a mano a
/// appsettings/env vars. Clave string en vez de PK sintética por si en el futuro hay más de un
/// asset de plataforma (favicon, logo de PDF, etc.) sin necesitar otra migración.
/// </summary>
public sealed class SystemAssetRef
{
    private SystemAssetRef() { }

    public string Key { get; private set; } = default!;
    public Guid CloudStorageFileId { get; private set; }
    public string ContentType { get; private set; } = default!;
    public long SizeBytes { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static SystemAssetRef Create(
        string key,
        Guid cloudStorageFileId,
        string contentType,
        long sizeBytes,
        DateTime updatedAtUtc
    ) =>
        new()
        {
            Key = key,
            CloudStorageFileId = cloudStorageFileId,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            UpdatedAtUtc = updatedAtUtc,
        };

    public void Update(Guid cloudStorageFileId, string contentType, long sizeBytes, DateTime updatedAtUtc)
    {
        CloudStorageFileId = cloudStorageFileId;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        UpdatedAtUtc = updatedAtUtc;
    }
}

/// <summary>Claves conocidas de <see cref="SystemAssetRef"/> — evita strings mágicos repetidos entre el seeder y LogoResolver.</summary>
public static class SystemAssetKeys
{
    public const string HeaderLogo = "header-logo";
}
