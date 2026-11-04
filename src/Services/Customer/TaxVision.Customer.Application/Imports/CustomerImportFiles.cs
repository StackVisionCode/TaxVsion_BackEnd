namespace TaxVision.Customer.Application.Imports;

/// <summary>
/// Cómo pide Customer a CloudStorage guardar el archivo de import (ver CustomerImportCloudStorageClient)
/// y cómo reconoce después el rechazo de ese mismo archivo. El FileId es el CustomerImportAttempt.Id.
/// </summary>
public static class CustomerImportFiles
{
    public const string RequestingService = "customer";
    public const string OwnerType = "Tenant";
    public const string FolderType = "Imports";

    public static bool IsImportFile(string? requestingService, string? folderType) =>
        string.Equals(requestingService, RequestingService, StringComparison.OrdinalIgnoreCase)
        && string.Equals(folderType, FolderType, StringComparison.OrdinalIgnoreCase);

    /// <summary>Motivo legible del rechazo de CloudStorage (códigos de SaveFileFromSourceHandler).</summary>
    public static string RejectionMessage(string? reason) =>
        reason switch
        {
            "EmptyFile" => "Uploaded file is empty.",
            "UploadPolicy" => "Uploaded file type or size is not allowed by the storage policy.",
            "StorageQuota.Exceeded" => "Storage quota is full; the uploaded file could not be saved.",
            "StorageQuota.Suspended" => "Storage is suspended; the uploaded file could not be saved.",
            _ => "Uploaded file could not be saved to storage.",
        };
}
