using BuildingBlocks.Results;

namespace TaxVision.CloudStorage.Domain.Folders;

public sealed record FolderName
{
    private FolderName(string value) => Value = value;

    public string Value { get; }

    public static Result<FolderName> Create(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > 255)
            return Result.Failure<FolderName>(FolderErrors.InvalidName);

        // El nombre se concatena en RelativePath (path materializado) — una barra
        // lo rompería en un segmento fantasma.
        if (trimmed.Contains('/') || trimmed.Contains('\\'))
            return Result.Failure<FolderName>(FolderErrors.InvalidName);

        return Result.Success(new FolderName(trimmed));
    }
}

public static class FolderErrors
{
    public static readonly Error InvalidName = new("Folder.InvalidName", "The folder name is invalid.");
    public static readonly Error OwnerRequired = new(
        "Folder.OwnerRequired",
        "An owner identifier is required for this owner type."
    );
    public static readonly Error NotFound = new("Folder.NotFound", "The folder was not found.");
    public static readonly Error ParentNotFound = new("Folder.ParentNotFound", "The parent folder was not found.");
    public static readonly Error OwnerMismatch = new(
        "Folder.OwnerMismatch",
        "The parent folder belongs to a different owner."
    );
    public static readonly Error NameAlreadyExists = new(
        "Folder.NameAlreadyExists",
        "A folder with this name already exists at this level."
    );
    public static readonly Error CircularReference = new(
        "Folder.CircularReference",
        "A folder cannot be moved into itself or one of its own descendants."
    );
    public static readonly Error Forbidden = new(
        "Folder.Forbidden",
        "The actor cannot access folders owned by another customer."
    );
}
