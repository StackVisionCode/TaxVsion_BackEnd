using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Application.Configuration;

public sealed class CloudStorageOptions
{
    public const string SectionName = "CloudStorage";

    public string MainBucket { get; set; } = "taxvision-storage";
    public string TempBucket { get; set; } = "taxvision-temp";
    public string QuarantineBucket { get; set; } = "taxvision-quarantine";
    public int PresignedUrlMinutes { get; set; } = 5;
    public int UploadReservationHours { get; set; } = 24;

    /// <summary>Fase C1 — dias que un archivo permanece en la papelera antes de que el job diario lo purgue definitivamente.</summary>
    public int RecycleBinRetentionDays { get; set; } = 30;
    public long DefaultStorageQuotaBytes { get; set; } = 10L * 1024 * 1024 * 1024;
    public long DefaultMaxFileSizeBytes { get; set; } = 25L * 1024 * 1024;

    /// <summary>
    /// Fallback para el override de FolderType.Recordings (ver StoragePlanPolicy.FolderOverridesBytes)
    /// cuando el plan del tenant no tiene una entrada configurada en PlanPolicies.
    /// </summary>
    public long DefaultMaxRecordingSizeBytes { get; set; } = 300L * 1024 * 1024;

    /// <summary>Fase B2 — cap duro de cantidad de archivos por descarga ZIP (413 si se supera).</summary>
    public int MaxZipFiles { get; set; } = 500;

    /// <summary>Fase B2 — cap duro de tamano agregado (suma de SizeBytes) por descarga ZIP (413 si se supera).</summary>
    public long MaxZipAggregateBytes { get; set; } = 500L * 1024 * 1024;

    /// <summary>
    /// Fase B2.1 — cap duro de cantidad de carpetas por descarga ZIP (413 si se
    /// supera). Se chequea ANTES de resolver el contenido de cada carpeta (folder
    /// pedido -> 1 query ListByPathPrefixAsync) para fallar barato antes de pagar
    /// el costo de I/O — MaxZipFiles sigue aplicando como tope real sobre el total
    /// de archivos ya resuelto (carpetas grandes con pocos IDs igual quedan acotadas).
    /// </summary>
    public int MaxZipFolders { get; set; } = 50;

    /// <summary>
    /// Fase U — tamano de cada parte en un multipart upload. 5MB es el minimo que
    /// exige la API S3 para cualquier parte que no sea la ultima (la ultima puede
    /// ser mas chica) — no bajar de eso o InitiateMultipartUpload/UploadPart fallan.
    /// </summary>
    public long MultipartPartSizeBytes { get; set; } = 5L * 1024 * 1024;
    public string[] AllowedExtensions { get; set; } =
    [
        ".pdf",
        ".doc",
        ".docx",
        ".xls",
        ".xlsx",
        ".ppt",
        ".pptx",
        ".txt",
        ".csv",
        ".rtf",
        ".jpg",
        ".jpeg",
        ".png",
        ".gif",
        ".webp",
        ".tif",
        ".tiff",
        ".zip",
        ".xml",
        ".json",
        ".html",
        ".webm",
        ".mp4",
    ];
    public string[] AllowedContentTypes { get; set; } =
    [
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-powerpoint",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "text/plain",
        "text/csv",
        "application/rtf",
        "image/jpeg",
        "image/png",
        "image/gif",
        "image/webp",
        "image/tiff",
        "application/zip",
        "application/xml",
        "text/xml",
        "application/json",
        "text/html",
        "video/webm",
        "video/mp4",
    ];

    /// <summary>
    /// Fase L1.1 — extensiones nunca permitidas, sin importar FolderType ni plan.
    /// Cubre ~95% del riesgo DMCA (contenido pirateable) y de malware ejecutable
    /// por diseño, sin analisis de contenido (ver CloudStorage_Service_Analysis_And_Design.pdf §19B.3).
    /// </summary>
    public string[] DangerousExtensions { get; set; } =
    [
        // Video pirateable
        ".mkv",
        ".avi",
        ".mov",
        ".mpg",
        ".mpeg",
        ".flv",
        ".wmv",
        ".m4v",
        // Audio pirateable
        ".mp3",
        ".flac",
        ".wav",
        ".aac",
        ".ape",
        ".wma",
        // Ejecutables
        ".exe",
        ".dll",
        ".msi",
        ".bat",
        ".cmd",
        ".com",
        ".scr",
        ".ps1",
        ".vbs",
        ".jar",
        ".apk",
        ".ipa",
        // Imagenes de disco
        ".iso",
        ".img",
        ".dmg",
        ".vmdk",
        ".vhd",
        // Piracy
        ".torrent",
    ];

    /// <summary>
    /// Fase L1.1 — whitelist granular por FolderType (extensiones + tamaño max).
    /// Es la restriccion primaria: la efectiva para un upload es la interseccion
    /// con el AllowedExtensions/AllowedContentTypes del plan (ver ResolveUploadPolicy),
    /// menos DangerousExtensions siempre.
    /// </summary>
    public Dictionary<FolderType, StorageFolderTypePolicy> FolderTypePolicies { get; set; } =
        new()
        {
            [FolderType.Documents] = OfficeDocumentsPolicy(),
            [FolderType.Receipts] = OfficeDocumentsPolicy(),
            [FolderType.Invoices] = OfficeDocumentsPolicy(),
            [FolderType.EmailIncoming] = OfficeDocumentsPolicy(),
            [FolderType.EmailOutgoing] = OfficeDocumentsPolicy(),
            [FolderType.Signatures] = OfficeDocumentsPolicy(),
            [FolderType.Tasks] = TaskAttachmentsPolicy(),
            [FolderType.Avatars] = AvatarsPolicy(),
            [FolderType.Imports] = ImportsExportsPolicy(),
            [FolderType.Recordings] = RecordingsPolicy(),
            [FolderType.Transcripts] = TranscriptsPolicy(),
            [FolderType.Backups] = BackupsPolicy(),
            [FolderType.Templates] = TemplatesPolicy(),
            [FolderType.Other] = OtherPolicy(),
        };

    public StoragePlanPolicy ResolvePlanPolicy(string planCode)
    {
        if (PlanPolicies.TryGetValue(planCode, out var configured))
            return configured;
        return new StoragePlanPolicy
        {
            MaxFileSizeBytes = DefaultMaxFileSizeBytes,
            AllowedExtensions = AllowedExtensions,
            AllowedContentTypes = AllowedContentTypes,
            FolderOverridesBytes = new(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(FolderType.Recordings)] = DefaultMaxRecordingSizeBytes,
            },
        };
    }

    /// <summary>Devuelve OtherPolicy() si folderType no tiene entrada configurada — nunca null, nunca sin restriccion.</summary>
    public StorageFolderTypePolicy ResolveFolderTypePolicy(FolderType folderType) =>
        FolderTypePolicies.TryGetValue(folderType, out var configured) ? configured : OtherPolicy();

    /// <summary>
    /// Politica efectiva para un upload: interseccion de lo que permite el plan
    /// del tenant y lo que permite el FolderType, menos DangerousExtensions
    /// siempre. Ni el plan mas caro ni un FolderType mal configurado pueden
    /// saltarse la blacklist global.
    ///
    /// El tope de tamano usa FolderOverridesBytes del plan cuando existe una entrada
    /// para este FolderType (ej. Recordings) en vez de MaxFileSizeBytes generico —
    /// una grabacion de meeting de mas de unos minutos siempre superaba el limite
    /// generico por-archivo del plan (pensado para documentos), aunque el propio
    /// FolderType.Recordings ya permitia hasta 500MB. Sigue acotado por
    /// folderPolicy.MaxSizeBytes: ni el override de un plan puede superar el tope
    /// duro del FolderType.
    /// </summary>
    public EffectiveUploadPolicy ResolveUploadPolicy(string planCode, FolderType folderType)
    {
        var planPolicy = ResolvePlanPolicy(planCode);
        var folderPolicy = ResolveFolderTypePolicy(folderType);

        var allowedExtensions = folderPolicy
            .AllowedExtensions.Intersect(planPolicy.AllowedExtensions, StringComparer.OrdinalIgnoreCase)
            .Except(DangerousExtensions, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var allowedContentTypes = folderPolicy
            .AllowedContentTypes.Intersect(planPolicy.AllowedContentTypes, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var planMaxSizeBytes = planPolicy.FolderOverridesBytes.TryGetValue(folderType.ToString(), out var overrideBytes)
            ? overrideBytes
            : planPolicy.MaxFileSizeBytes;

        return new EffectiveUploadPolicy(
            Math.Min(folderPolicy.MaxSizeBytes, planMaxSizeBytes),
            allowedExtensions,
            allowedContentTypes
        );
    }

    public Dictionary<string, StoragePlanPolicy> PlanPolicies { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    private static StorageFolderTypePolicy OfficeDocumentsPolicy() =>
        new()
        {
            MaxSizeBytes = 25L * 1024 * 1024,
            AllowedExtensions =
            [
                ".pdf",
                ".doc",
                ".docx",
                ".xls",
                ".xlsx",
                ".ppt",
                ".pptx",
                ".txt",
                ".csv",
                ".rtf",
                ".xml",
                ".json",
                ".jpg",
                ".jpeg",
                ".png",
                ".tif",
                ".tiff",
                ".zip",
            ],
            AllowedContentTypes =
            [
                "application/pdf",
                "application/msword",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "application/vnd.ms-excel",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "application/vnd.ms-powerpoint",
                "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                "text/plain",
                "text/csv",
                "application/rtf",
                "application/xml",
                "text/xml",
                "application/json",
                "image/jpeg",
                "image/png",
                "image/tiff",
                "application/zip",
            ],
        };

    private static StorageFolderTypePolicy TaskAttachmentsPolicy() =>
        new()
        {
            MaxSizeBytes = 25L * 1024 * 1024,
            AllowedExtensions = [".pdf", ".docx", ".xlsx", ".jpg", ".jpeg", ".png", ".txt", ".zip"],
            AllowedContentTypes =
            [
                "application/pdf",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "image/jpeg",
                "image/png",
                "text/plain",
                "application/zip",
            ],
        };

    private static StorageFolderTypePolicy AvatarsPolicy() =>
        new()
        {
            MaxSizeBytes = 5L * 1024 * 1024,
            AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"],
            AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"],
        };

    private static StorageFolderTypePolicy ImportsExportsPolicy() =>
        new()
        {
            MaxSizeBytes = 100L * 1024 * 1024,
            AllowedExtensions = [".csv", ".xlsx", ".zip"],
            AllowedContentTypes =
            [
                "text/csv",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "application/zip",
            ],
        };

    /// <summary>
    /// Grabaciones de calls/meetings. El PDF exige que solo lleguen via eventos
    /// del servidor de Communication, nunca upload directo de usuario — esa
    /// restriccion de ACTOR queda fuera de L1.1 (es de la Fase D, control de
    /// acceso) y se documenta como gap conocido en el reporte de fase.
    /// </summary>
    private static StorageFolderTypePolicy RecordingsPolicy() =>
        new()
        {
            MaxSizeBytes = 500L * 1024 * 1024,
            AllowedExtensions = [".webm", ".mp4"],
            AllowedContentTypes = ["video/webm", "video/mp4"],
        };

    /// <summary>
    /// Transcripts .txt generados por whisper.cpp a partir de una grabacion.
    /// Texto plano, chico incluso para audio largo (una transcripcion de 2
    /// horas ronda unos cientos de KB) — 5MB da margen de sobra sin acercarse
    /// al costo de una grabacion real.
    /// </summary>
    private static StorageFolderTypePolicy TranscriptsPolicy() =>
        new()
        {
            MaxSizeBytes = 5L * 1024 * 1024,
            AllowedExtensions = [".txt"],
            AllowedContentTypes = ["text/plain"],
        };

    /// <summary>
    /// HTML/text/design-JSON/preview-PNG de EmailTemplateVersion y EmailLayoutVersion (Scribe Fase 5).
    /// Contenido chico por naturaleza (un layout HTML rara vez pasa de unos pocos KB); 5MB da margen
    /// generoso sin acercarse a un vector de abuso.
    /// </summary>
    private static StorageFolderTypePolicy TemplatesPolicy() =>
        new()
        {
            MaxSizeBytes = 5L * 1024 * 1024,
            AllowedExtensions = [".html", ".txt", ".json", ".png"],
            AllowedContentTypes = ["text/html", "text/plain", "application/json", "image/png"],
        };

    private static StorageFolderTypePolicy BackupsPolicy() =>
        new()
        {
            MaxSizeBytes = 200L * 1024 * 1024,
            AllowedExtensions = [".zip", ".json", ".csv"],
            AllowedContentTypes = ["application/zip", "application/json", "text/csv"],
        };

    private static StorageFolderTypePolicy OtherPolicy() =>
        new()
        {
            MaxSizeBytes = 25L * 1024 * 1024,
            AllowedExtensions = [".pdf", ".docx", ".xlsx", ".jpg", ".jpeg", ".png", ".txt", ".csv", ".zip", ".json"],
            AllowedContentTypes =
            [
                "application/pdf",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "image/jpeg",
                "image/png",
                "text/plain",
                "text/csv",
                "application/zip",
                "application/json",
            ],
        };
}

public sealed class StoragePlanPolicy
{
    public long MaxFileSizeBytes { get; set; }
    public string[] AllowedExtensions { get; set; } = [];
    public string[] AllowedContentTypes { get; set; } = [];

    /// <summary>
    /// Override de MaxFileSizeBytes por FolderType (clave = FolderType.ToString(), ej. "Recordings").
    /// Cuando existe una entrada para el FolderType del upload, reemplaza MaxFileSizeBytes en
    /// ResolveUploadPolicy — sigue acotado por el MaxSizeBytes propio del FolderType.
    /// </summary>
    public Dictionary<string, long> FolderOverridesBytes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Fase L1.1 — whitelist de un FolderType puntual, antes de intersectar con el plan del tenant.</summary>
public sealed class StorageFolderTypePolicy
{
    public long MaxSizeBytes { get; set; }
    public string[] AllowedExtensions { get; set; } = [];
    public string[] AllowedContentTypes { get; set; } = [];
}

/// <summary>Politica ya resuelta para un upload puntual — interseccion de plan + FolderType, menos la blacklist global.</summary>
public sealed record EffectiveUploadPolicy(
    long MaxFileSizeBytes,
    IReadOnlyCollection<string> AllowedExtensions,
    IReadOnlyCollection<string> AllowedContentTypes
);
