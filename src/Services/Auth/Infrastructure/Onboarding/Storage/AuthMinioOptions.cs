namespace TaxVision.Auth.Infrastructure.Onboarding.Storage;

/// <summary>
/// Credenciales MinIO propias de Auth (IAM scoped a s3:PutObject en taxvision-temp/auth/*, ver
/// deploy/docker/minio/policies/auth-source.json). Nunca las credenciales root de CloudStorage.
/// Primer uso de MinIO en Auth — documentos legales (ToS/Privacy Policy) subidos por PlatformAdmin.
/// </summary>
public sealed class AuthMinioOptions
{
    public const string SectionName = "Auth:Minio";

    public string Endpoint { get; set; } = "localhost:9000";
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public bool UseTls { get; set; }
    public string TempBucket { get; set; } = "taxvision-temp";
    public string SourcePrefix { get; set; } = "auth";
}
