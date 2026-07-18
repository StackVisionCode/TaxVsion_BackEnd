namespace TaxVision.Correspondence.Infrastructure.CloudStorage;

/// <summary>Base URL del microservicio CloudStorage (M2M ServiceOnly).</summary>
public sealed class CloudStorageClientOptions
{
    public const string SectionName = "Correspondence:CloudStorage";

    public string BaseUrl { get; set; } = "http://localhost:5210";
}
