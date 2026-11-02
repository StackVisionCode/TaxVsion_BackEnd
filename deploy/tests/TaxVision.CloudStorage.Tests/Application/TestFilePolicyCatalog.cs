using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.FileTypes;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>
/// El mismo catálogo y las mismas políticas por carpeta que siembra la migración
/// AddFileTypeCatalogAndFolderPolicies (que a su vez replica lo que vivía en CloudStorageOptions).
/// </summary>
internal static class TestFilePolicyCatalog
{
    private const long Mb = 1024 * 1024;

    private static readonly (string Extension, string Group, string[] ContentTypes)[] Types =
    [
        (".pdf", "documents", ["application/pdf"]),
        (".doc", "documents", ["application/msword"]),
        (".docx", "documents", ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"]),
        (".txt", "documents", ["text/plain"]),
        (".xls", "spreadsheets", ["application/vnd.ms-excel"]),
        (".xlsx", "spreadsheets", ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"]),
        (".csv", "spreadsheets", ["text/csv"]),
        (".jpg", "images", ["image/jpeg"]),
        (".jpeg", "images", ["image/jpeg"]),
        (".png", "images", ["image/png"]),
        (".tif", "images", ["image/tiff"]),
        (".tiff", "images", ["image/tiff"]),
        (".json", "data", ["application/json"]),
        (".html", "data", ["text/html"]),
        (".webm", "media", ["video/webm", "audio/webm"]),
        (".mp4", "media", ["video/mp4", "audio/mp4"]),
        (".ppt", "presentations", ["application/vnd.ms-powerpoint"]),
        (".pptx", "presentations", ["application/vnd.openxmlformats-officedocument.presentationml.presentation"]),
        (".rtf", "rich_text", ["application/rtf"]),
        (".gif", "web_images", ["image/gif"]),
        (".webp", "web_images", ["image/webp"]),
        (".zip", "archives", ["application/zip"]),
        (".xml", "xml", ["application/xml", "text/xml"]),
        (".svg", "branding", ["image/svg+xml"]),
    ];

    private static readonly string[] Office =
    [
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".csv", ".rtf", ".xml", ".json", ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".zip",
    ];

    private static readonly (FolderType Folder, long Max, string[] Extensions, bool PlanTypes, bool PlanSize)[] Folders =
    [
        (FolderType.Documents, 25 * Mb, Office, true, true),
        (FolderType.Receipts, 25 * Mb, Office, true, true),
        (FolderType.Invoices, 25 * Mb, Office, true, true),
        (FolderType.EmailIncoming, 25 * Mb, Office, true, true),
        (FolderType.EmailOutgoing, 25 * Mb, Office, true, true),
        (FolderType.Signatures, 25 * Mb, Office, true, true),
        (FolderType.Tasks, 25 * Mb, [".pdf", ".docx", ".xlsx", ".jpg", ".jpeg", ".png", ".txt", ".zip"], true, true),
        (FolderType.Avatars, 5 * Mb, [".jpg", ".jpeg", ".png", ".webp"], false, false),
        (FolderType.Imports, 100 * Mb, [".csv", ".xlsx", ".zip"], true, true),
        (FolderType.Recordings, 500 * Mb, [".webm", ".mp4"], false, false),
        (FolderType.RecordingTracks, 500 * Mb, [".webm"], false, false),
        (FolderType.Transcripts, 5 * Mb, [".txt", ".json"], true, true),
        (FolderType.Templates, 5 * Mb, [".html", ".txt", ".json", ".png"], true, true),
        (FolderType.Branding, 500 * 1024, [".png", ".jpg", ".jpeg", ".svg"], false, false),
        (FolderType.SaaSReceipts, 5 * Mb, [".pdf"], false, false),
        (FolderType.ClientForms, 25 * Mb, [".pdf", ".jpg", ".jpeg", ".png", ".docx", ".xlsx", ".csv", ".txt"], true, true),
        (FolderType.Backups, 200 * Mb, [".zip", ".json", ".csv"], true, true),
        (FolderType.VoiceNotes, 50 * Mb, [".webm", ".mp4"], false, false),
        (FolderType.Other, 25 * Mb, [".pdf", ".docx", ".xlsx", ".jpg", ".jpeg", ".png", ".txt", ".csv", ".zip", ".json"], true, true),
    ];

    public static IReadOnlyList<FileTypeDefinition> FileTypes() =>
        Types.Select(type => FileTypeDefinition.Create(type.Extension, type.Group, type.ContentTypes, Guid.Empty, DateTime.UtcNow).Value).ToList();

    public static IReadOnlyDictionary<FolderType, FolderFilePolicy> FolderPolicies() =>
        Folders.ToDictionary(
            folder => folder.Folder,
            folder => FolderFilePolicy.Create(folder.Folder, folder.Max, folder.Extensions, folder.PlanTypes, folder.PlanSize, Guid.Empty, DateTime.UtcNow).Value
        );

    public static FolderFilePolicy Folder(FolderType folderType) => FolderPolicies()[folderType];
}
