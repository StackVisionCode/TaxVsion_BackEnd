using System.Text.RegularExpressions;
using BuildingBlocks.Results;

namespace TaxVision.CloudStorage.Domain.Files;

public sealed record ObjectKey
{
    private static readonly Regex SafeKey = new(
        @"^[a-zA-Z0-9][a-zA-Z0-9._/-]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    private ObjectKey(string value) => Value = value;

    public string Value { get; }

    public static Result<ObjectKey> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1024)
            return Result.Failure<ObjectKey>(FileErrors.InvalidObjectKey);

        if (
            value.StartsWith('/')
            || value.EndsWith('/')
            || value.Contains("..", StringComparison.Ordinal)
            || value.Contains("//", StringComparison.Ordinal)
            || value.Contains('\\')
            || !SafeKey.IsMatch(value)
        )
            return Result.Failure<ObjectKey>(FileErrors.InvalidObjectKey);

        return Result.Success(new ObjectKey(value));
    }
}

public sealed record ChecksumSha256
{
    private static readonly Regex Sha256 = new("^[a-f0-9]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private ChecksumSha256(string value) => Value = value;

    public string Value { get; }

    public static Result<ChecksumSha256> Create(string value) =>
        Sha256.IsMatch(value)
            ? Result.Success(new ChecksumSha256(value))
            : Result.Failure<ChecksumSha256>(FileErrors.InvalidChecksum);
}

public static class FileErrors
{
    public static readonly Error InvalidObjectKey = new(
        "File.InvalidObjectKey",
        "The generated object key is invalid."
    );
    public static readonly Error InvalidChecksum = new("File.InvalidChecksum", "The SHA-256 checksum is invalid.");
    public static readonly Error InvalidSize = new("File.InvalidSize", "File size must be greater than zero.");
    public static readonly Error YearRequired = new(
        "File.YearRequired",
        "A tax year is required for this folder type."
    );
    public static readonly Error OwnerRequired = new(
        "File.OwnerRequired",
        "An owner identifier is required for this owner type."
    );
    public static readonly Error InvalidTransition = new(
        "File.InvalidTransition",
        "The requested file status transition is invalid."
    );
    public static readonly Error NotFound = new("File.NotFound", "The file was not found.");
    public static readonly Error Forbidden = new(
        "File.Forbidden",
        "The actor cannot access files owned by another customer."
    );
    public static readonly Error NotAvailable = new("File.NotAvailable", "The file has not passed the security scan.");
    public static readonly Error UploadSizeMismatch = new(
        "File.UploadSizeMismatch",
        "The uploaded object size does not match the declared size."
    );
    public static readonly Error UnsupportedType = new(
        "File.UnsupportedType",
        "The file type is not allowed or does not match its content."
    );
    public static readonly Error LegalHold = new("File.LegalHold", "The file is under legal hold.");
    public static readonly Error AlreadyLegalHeld = new(
        "File.AlreadyLegalHeld",
        "The file is already under legal hold."
    );
    public static readonly Error NotLegalHeld = new("File.NotLegalHeld", "The file is not under legal hold.");

    /// <summary>Fase B2 — ZIP download excede el cap duro de cantidad de archivos.</summary>
    public static readonly Error TooManyItems = new("File.TooManyItems", "Too many files requested for a single ZIP.");

    /// <summary>Fase B2 — ZIP download excede el cap duro de tamano agregado.</summary>
    public static readonly Error ZipTooLarge = new(
        "File.ZipTooLarge",
        "The combined size of the requested files exceeds the ZIP download limit."
    );

    /// <summary>Fase B2 — el body no trajo ningun fileId ni folderId.</summary>
    public static readonly Error NoFilesRequested = new(
        "File.NoFilesRequested",
        "At least one file or folder must be requested."
    );

    /// <summary>Fase B2.1 — fileIds+folderIds validos pero sin ningun archivo Available para incluir (carpetas vacias o aun escaneando).</summary>
    public static readonly Error NoFilesResolved = new(
        "File.NoFilesResolved",
        "The requested files and folders did not resolve to any downloadable file."
    );

    /// <summary>Fase B2.1 — cap duro de cantidad de carpetas por descarga ZIP (413 si se supera), chequeado antes de resolver su contenido.</summary>
    public static readonly Error TooManyFolders = new(
        "File.TooManyFolders",
        "Too many folders requested for a single ZIP."
    );

    /// <summary>Fase U — el ensamblado final del multipart upload en el storage fallo (ETags invalidos, parte faltante, etc).</summary>
    public static readonly Error MultipartCompleteFailed = new(
        "File.MultipartCompleteFailed",
        "The multipart upload could not be completed."
    );
}
