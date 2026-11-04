using System.Text.RegularExpressions;
using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.CloudStorage.Domain.FileTypes;

/// <summary>
/// Un tipo de archivo del catálogo de la plataforma: su extensión, el grupo que vende un plan
/// (<c>documents</c>, <c>images</c>…) y los MIME que se aceptan para ella. Lo administra PlatformAdmin;
/// agregar un tipo no requiere redeploy. No es de ningún tenant.
/// </summary>
public sealed partial class FileTypeDefinition : BaseEntity
{
    public const int MaxContentTypesLength = 1000;

    private FileTypeDefinition() { }

    public string Extension { get; private set; } = default!;
    public string Group { get; private set; } = default!;

    /// <summary>MIME aceptados (declarado por el navegador y detectado por magic bytes), separados por coma.</summary>
    public string ContentTypes { get; private set; } = default!;

    public bool IsEnabled { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid UpdatedByUserId { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyList<string> ContentTypeList => ContentTypes.Split(',', StringSplitOptions.RemoveEmptyEntries);

    public static Result<FileTypeDefinition> Create(
        string extension,
        string group,
        IReadOnlyCollection<string> contentTypes,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        var normalized = NormalizeExtension(extension);
        if (normalized.IsFailure)
            return Result.Failure<FileTypeDefinition>(normalized.Error);

        var definition = new FileTypeDefinition { Id = Guid.NewGuid(), Extension = normalized.Value };
        var updated = definition.Update(group, contentTypes, enabled: true, actorUserId, nowUtc);
        return updated.IsFailure ? Result.Failure<FileTypeDefinition>(updated.Error) : Result.Success(definition);
    }

    public Result Update(
        string group,
        IReadOnlyCollection<string> contentTypes,
        bool enabled,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        var normalizedGroup = group?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!GroupPattern().IsMatch(normalizedGroup))
            return Result.Failure(FileTypeErrors.InvalidGroup);

        var types = (contentTypes ?? []).Select(type => type.Trim().ToLowerInvariant()).Distinct().ToList();
        if (types.Count == 0 || types.Any(type => !ContentTypePattern().IsMatch(type)))
            return Result.Failure(FileTypeErrors.InvalidContentType);
        var joined = string.Join(',', types);
        if (joined.Length > MaxContentTypesLength)
            return Result.Failure(FileTypeErrors.InvalidContentType);

        Group = normalizedGroup;
        ContentTypes = joined;
        IsEnabled = enabled;
        UpdatedByUserId = actorUserId;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public static Result<string> NormalizeExtension(string? extension)
    {
        var value = extension?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!value.StartsWith('.'))
            value = "." + value;
        if (!ExtensionPattern().IsMatch(value))
            return Result.Failure<string>(FileTypeErrors.InvalidExtension);
        return DangerousFileTypes.IsDangerous(value)
            ? Result.Failure<string>(FileTypeErrors.DangerousExtension)
            : Result.Success(value);
    }

    [GeneratedRegex(@"^\.[a-z0-9]{1,10}$")]
    private static partial Regex ExtensionPattern();

    [GeneratedRegex(@"^[a-z][a-z0-9_]{1,39}$")]
    private static partial Regex GroupPattern();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9.+-]*/[a-z0-9][a-z0-9.+-]*$")]
    private static partial Regex ContentTypePattern();
}
