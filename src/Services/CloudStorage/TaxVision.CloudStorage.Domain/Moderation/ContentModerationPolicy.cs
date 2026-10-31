using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Domain.Moderation;

/// <summary>Lo que CloudStorage hace con un archivo según la puntuación sexual que da el scanner.</summary>
public enum ModerationDecision
{
    Clean,
    Review,
    Block,
}

/// <summary>Valores editables de la política; se validan juntos porque los umbrales dependen entre sí.</summary>
public sealed record ContentModerationSettings(
    bool Enabled,
    double BlockThreshold,
    double ReviewThreshold,
    bool ScanVideos,
    int FrameIntervalSeconds,
    int MaxFrames,
    long MaxScanBytes,
    IReadOnlyCollection<FolderType> ExcludedFolders,
    bool FailClosed
)
{
    /// <summary>Solo imágenes y (si está activo) videos, por tipo DETECTADO, fuera de las carpetas excluidas.</summary>
    public bool AppliesTo(FolderType folderType, string? detectedContentType)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(detectedContentType) || ExcludedFolders.Contains(folderType))
            return false;
        if (detectedContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return true;
        return ScanVideos && detectedContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
    }

    public ModerationDecision Decide(double sexualScore) =>
        sexualScore >= BlockThreshold ? ModerationDecision.Block
        : sexualScore >= ReviewThreshold ? ModerationDecision.Review
        : ModerationDecision.Clean;
}

/// <summary>
/// Política de moderación de contenido de la plataforma (T-20): una sola fila en BD que administra
/// PlatformAdmin. El scanner solo devuelve puntuaciones; los umbrales y el alcance se deciden acá.
/// No es <see cref="TenantEntity"/>: aplica a todas las oficinas por igual.
/// </summary>
public sealed class ContentModerationPolicy : BaseEntity
{
    /// <summary>Id fijo de la única fila; la siembra la migración que crea la tabla.</summary>
    public static readonly Guid PlatformPolicyId = new("5c0d7e1a-7f3b-4c2e-9a51-0b6f1d2e3c40");

    public const int MaxFrameIntervalSeconds = 60;
    public const int MaxFramesLimit = 120;

    private ContentModerationPolicy() { }

    public bool Enabled { get; private set; }
    public double BlockThreshold { get; private set; }
    public double ReviewThreshold { get; private set; }
    public bool ScanVideos { get; private set; }
    public int FrameIntervalSeconds { get; private set; }
    public int MaxFrames { get; private set; }
    public long MaxScanBytes { get; private set; }

    /// <summary>Nombres de FolderType separados por coma (persistido); usar <see cref="ExcludedFolderTypes"/>.</summary>
    public string ExcludedFolders { get; private set; } = string.Empty;

    /// <summary>true: si el scanner falla o no está configurado, el archivo queda en revisión en vez de publicarse.</summary>
    public bool FailClosed { get; private set; }

    public Guid UpdatedByUserId { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlySet<FolderType> ExcludedFolderTypes => ParseFolders(ExcludedFolders);

    public ContentModerationSettings ToSettings() =>
        new(
            Enabled,
            BlockThreshold,
            ReviewThreshold,
            ScanVideos,
            FrameIntervalSeconds,
            MaxFrames,
            MaxScanBytes,
            ExcludedFolderTypes.OrderBy(f => f.ToString(), StringComparer.Ordinal).ToList(),
            FailClosed
        );

    /// <summary>La fila de producción la siembra la migración; esto crea una equivalente (re-siembra, tests).</summary>
    public static Result<ContentModerationPolicy> Create(
        ContentModerationSettings settings,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        var policy = new ContentModerationPolicy { Id = PlatformPolicyId };
        var updated = policy.Update(settings, actorUserId, nowUtc);
        return updated.IsSuccess ? Result.Success(policy) : Result.Failure<ContentModerationPolicy>(updated.Error);
    }

    public Result Update(ContentModerationSettings settings, Guid actorUserId, DateTime nowUtc)
    {
        var valid = Validate(settings);
        if (valid.IsFailure)
            return valid;
        Enabled = settings.Enabled;
        BlockThreshold = settings.BlockThreshold;
        ReviewThreshold = settings.ReviewThreshold;
        ScanVideos = settings.ScanVideos;
        FrameIntervalSeconds = settings.FrameIntervalSeconds;
        MaxFrames = settings.MaxFrames;
        MaxScanBytes = settings.MaxScanBytes;
        ExcludedFolders = FormatFolders(settings.ExcludedFolders);
        FailClosed = settings.FailClosed;
        UpdatedByUserId = actorUserId;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public static Result Validate(ContentModerationSettings settings)
    {
        if (
            double.IsNaN(settings.ReviewThreshold)
            || double.IsNaN(settings.BlockThreshold)
            || settings.ReviewThreshold <= 0
            || settings.ReviewThreshold > settings.BlockThreshold
            || settings.BlockThreshold > 1
        )
            return Result.Failure(ContentModerationErrors.InvalidThresholds);
        if (settings.FrameIntervalSeconds is < 1 or > MaxFrameIntervalSeconds)
            return Result.Failure(ContentModerationErrors.InvalidFrameInterval);
        if (settings.MaxFrames is < 1 or > MaxFramesLimit)
            return Result.Failure(ContentModerationErrors.InvalidMaxFrames);
        if (settings.MaxScanBytes < 1 || settings.MaxScanBytes > UploadSizePolicy.TechnicalMaxBytes)
            return Result.Failure(ContentModerationErrors.InvalidMaxScanBytes);
        if (settings.ExcludedFolders is null || settings.ExcludedFolders.Any(f => !Enum.IsDefined(f)))
            return Result.Failure(ContentModerationErrors.InvalidFolderType);
        return Result.Success();
    }

    public static string FormatFolders(IEnumerable<FolderType> folders) =>
        string.Join(',', folders.Distinct().Select(f => f.ToString()).OrderBy(f => f, StringComparer.Ordinal));

    private static HashSet<FolderType> ParseFolders(string raw) =>
        raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name =>
                Enum.TryParse<FolderType>(name, ignoreCase: false, out var folder) ? folder : (FolderType?)null
            )
            .Where(folder => folder is not null)
            .Select(folder => folder!.Value)
            .ToHashSet();
}

/// <summary>Una fila por cambio de política: foto antes/después para saber quién relajó o endureció qué.</summary>
public sealed class ContentModerationPolicyAudit : BaseEntity
{
    private ContentModerationPolicyAudit() { }

    public string OldSettingsJson { get; private set; } = default!;
    public string NewSettingsJson { get; private set; } = default!;
    public Guid ActorUserId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }

    public static ContentModerationPolicyAudit Record(
        string oldSettingsJson,
        string newSettingsJson,
        Guid actorUserId,
        DateTime occurredAtUtc
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            OldSettingsJson = oldSettingsJson,
            NewSettingsJson = newSettingsJson,
            ActorUserId = actorUserId,
            OccurredAtUtc = occurredAtUtc,
        };
}

public static class ContentModerationErrors
{
    public static readonly Error InvalidThresholds = new(
        "ContentModeration.InvalidThresholds",
        "Thresholds must satisfy 0 < review threshold <= block threshold <= 1."
    );
    public static readonly Error InvalidFrameInterval = new(
        "ContentModeration.InvalidFrameInterval",
        $"The frame interval must be between 1 and {ContentModerationPolicy.MaxFrameIntervalSeconds} seconds."
    );
    public static readonly Error InvalidMaxFrames = new(
        "ContentModeration.InvalidMaxFrames",
        $"The frame cap must be between 1 and {ContentModerationPolicy.MaxFramesLimit}."
    );
    public static readonly Error InvalidMaxScanBytes = new(
        "ContentModeration.InvalidMaxScanBytes",
        "The maximum scanned size must be between 1 byte and 5 GB."
    );
    public static readonly Error InvalidFolderType = new(
        "ContentModeration.InvalidFolderType",
        "Excluded folders must be known folder types."
    );
    public static readonly Error PolicyMissing = new(
        "ContentModeration.PolicyMissing",
        "The platform content moderation policy row is missing."
    );
    public static readonly Error NotUnderModeration = new(
        "ContentModeration.NotUnderModeration",
        "The file is not pending review or blocked by the content policy."
    );
}
