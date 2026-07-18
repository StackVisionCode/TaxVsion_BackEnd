namespace TaxVision.Correspondence.Infrastructure.CloudStorage;

/// <summary>
/// Fase 8 — credenciales MinIO propias de Correspondence (IAM scoped a
/// s3:PutObject en taxvision-temp/correspondence/*, ver deploy/docker/minio/policies/
/// correspondence-source.json). Mismo patrón D0/D1 que Signature/Notification/Customer;
/// nunca las credenciales root de CloudStorage.
/// </summary>
public sealed class CorrespondenceMinioOptions
{
    public const string SectionName = "Correspondence:Minio";

    public string Endpoint { get; set; } = "localhost:9000";
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public bool UseTls { get; set; }
    public string TempBucket { get; set; } = "taxvision-temp";
    public string SourcePrefix { get; set; } = "correspondence";
}
