namespace TaxVision.Tenant.Infrastructure.Branding;

public sealed class ServiceAuthClientOptions
{
    public const string SectionName = "ServiceAuthClient";

    /// <summary>Base URL del servicio Auth. En Docker: http://auth-api:8080.</summary>
    public string AuthBaseUrl { get; set; } = "http://localhost:5124";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

public sealed class CloudStorageClientOptions
{
    public const string SectionName = "CloudStorageClient";

    /// <summary>Base URL de CloudStorage. En Docker: http://cloudstorage-api:8080.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5330";
}

public sealed class TenantMinioOptions
{
    public const string SectionName = "Tenant:Minio";

    public string Endpoint { get; set; } = "localhost:9000";
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public bool UseTls { get; set; }
    public string TempBucket { get; set; } = "taxvision-temp";
    public string SourcePrefix { get; set; } = "tenant-branding";
}
