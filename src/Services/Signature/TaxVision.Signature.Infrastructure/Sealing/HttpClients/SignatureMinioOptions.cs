namespace TaxVision.Signature.Infrastructure.Sealing.HttpClients;

/// <summary>
/// Fase D0/D1 — credenciales MinIO propias de Signature (IAM scoped a
/// s3:PutObject en taxvision-temp/signature/*, ver deploy/docker/minio/policies/
/// signature-source.json). Nunca las credenciales root de CloudStorage.
/// </summary>
public sealed class SignatureMinioOptions
{
    public const string SectionName = "Signature:Minio";

    public string Endpoint { get; set; } = "localhost:9000";
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public bool UseTls { get; set; }
    public string TempBucket { get; set; } = "taxvision-temp";
    public string SourcePrefix { get; set; } = "signature";
}
