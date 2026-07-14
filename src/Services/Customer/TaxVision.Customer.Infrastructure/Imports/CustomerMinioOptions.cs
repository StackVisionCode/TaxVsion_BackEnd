namespace TaxVision.Customer.Infrastructure.Imports;

/// <summary>
/// Credenciales MinIO propias de Customer (IAM scoped a s3:PutObject en
/// taxvision-temp/customer/*, ver deploy/docker/minio/policies/customer-source.json).
/// Nunca las credenciales root de CloudStorage.
/// </summary>
public sealed class CustomerMinioOptions
{
    public const string SectionName = "Customer:Minio";

    public string Endpoint { get; set; } = "localhost:9000";
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public bool UseTls { get; set; }
    public string TempBucket { get; set; } = "taxvision-temp";
    public string SourcePrefix { get; set; } = "customer";
}
