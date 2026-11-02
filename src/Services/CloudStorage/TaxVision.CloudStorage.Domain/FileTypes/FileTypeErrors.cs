using BuildingBlocks.Results;

namespace TaxVision.CloudStorage.Domain.FileTypes;

public static class FileTypeErrors
{
    public static readonly Error InvalidExtension = new(
        "FileType.InvalidExtension",
        "The extension must look like .pdf (a dot and 1–10 letters or digits)."
    );

    public static readonly Error DangerousExtension = new(
        "FileType.DangerousExtension",
        "This extension is blocked for security and can't be allowed."
    );

    public static readonly Error InvalidGroup = new(
        "FileType.InvalidGroup",
        "The group must be lowercase letters, digits or underscores (2–40 characters)."
    );

    public static readonly Error InvalidContentType = new(
        "FileType.InvalidContentType",
        "Each content type must look like type/subtype, and at least one is required."
    );

    public static readonly Error InvalidMaxSize = new(
        "FileType.InvalidMaxSize",
        "The folder size limit must be between 1 byte and the 5 GB technical ceiling."
    );

    public static readonly Error UnknownGroup = new("FileType.UnknownGroup", "One or more file type groups don't exist.");

    public static readonly Error UnknownExtension = new(
        "FileType.UnknownExtension",
        "Every extension of a folder policy must exist in the file type catalog."
    );
}
