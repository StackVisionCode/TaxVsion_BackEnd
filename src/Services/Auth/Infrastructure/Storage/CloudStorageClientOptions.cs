namespace TaxVision.Auth.Infrastructure.Storage;

public sealed class CloudStorageClientOptions
{
    public const string SectionName = "Auth:CloudStorage";

    public string BaseUrl { get; set; } = "http://localhost:5330";
}
