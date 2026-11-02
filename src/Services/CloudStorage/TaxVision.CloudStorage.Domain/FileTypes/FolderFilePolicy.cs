using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Domain.FileTypes;

/// <summary>
/// Qué admite cada carpeta de sistema (FolderType): su techo de tamaño, sus extensiones y si además se
/// recorta con lo que vende el plan. Las carpetas de SISTEMA (logo, avatares, notas de voz, recibos,
/// grabaciones) no dependen del plan: su contenido lo genera la plataforma, no lo elige el usuario.
/// Es de la plataforma (PlatformAdmin), no de un tenant.
/// </summary>
public sealed class FolderFilePolicy : BaseEntity
{
    public const int MaxExtensionsLength = 1000;

    private FolderFilePolicy() { }

    public FolderType FolderType { get; private set; }
    public long MaxSizeBytes { get; private set; }

    /// <summary>Extensiones admitidas, separadas por coma (todas deben existir en el catálogo).</summary>
    public string AllowedExtensions { get; private set; } = default!;

    /// <summary>true = solo las extensiones cuyo grupo vende el plan del tenant.</summary>
    public bool UsesPlanFileTypes { get; private set; }

    /// <summary>true = el tope es el menor entre esta carpeta y el tamaño por archivo del plan.</summary>
    public bool UsesPlanFileSize { get; private set; }

    public Guid UpdatedByUserId { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyList<string> ExtensionList => AllowedExtensions.Split(',', StringSplitOptions.RemoveEmptyEntries);

    public static Result<FolderFilePolicy> Create(
        FolderType folderType,
        long maxSizeBytes,
        IReadOnlyCollection<string> extensions,
        bool usesPlanFileTypes,
        bool usesPlanFileSize,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        var policy = new FolderFilePolicy { Id = Guid.NewGuid(), FolderType = folderType };
        var updated = policy.Update(maxSizeBytes, extensions, usesPlanFileTypes, usesPlanFileSize, actorUserId, nowUtc);
        return updated.IsFailure ? Result.Failure<FolderFilePolicy>(updated.Error) : Result.Success(policy);
    }

    public Result Update(
        long maxSizeBytes,
        IReadOnlyCollection<string> extensions,
        bool usesPlanFileTypes,
        bool usesPlanFileSize,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (maxSizeBytes is < 1 or > UploadSizePolicy.TechnicalMaxBytes)
            return Result.Failure(FileTypeErrors.InvalidMaxSize);

        var normalized = new List<string>();
        foreach (var extension in extensions ?? [])
        {
            var value = FileTypeDefinition.NormalizeExtension(extension);
            if (value.IsFailure)
                return Result.Failure(value.Error);
            if (!normalized.Contains(value.Value))
                normalized.Add(value.Value);
        }
        var joined = string.Join(',', normalized);
        if (joined.Length > MaxExtensionsLength)
            return Result.Failure(FileTypeErrors.InvalidExtension);

        MaxSizeBytes = maxSizeBytes;
        AllowedExtensions = joined;
        UsesPlanFileTypes = usesPlanFileTypes;
        UsesPlanFileSize = usesPlanFileSize;
        UpdatedByUserId = actorUserId;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }
}
