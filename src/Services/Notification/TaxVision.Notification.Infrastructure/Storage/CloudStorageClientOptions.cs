namespace TaxVision.Notification.Infrastructure.Storage;

public sealed class CloudStorageClientOptions
{
    public const string SectionName = "CloudStorageClient";

    /// <summary>Base URL del microservicio CloudStorage (directo o vía Gateway). En Docker: http://cloudstorage-api:8080.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5330";
}
