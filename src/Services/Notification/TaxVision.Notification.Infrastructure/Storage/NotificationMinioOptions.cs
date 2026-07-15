namespace TaxVision.Notification.Infrastructure.Storage;

/// <summary>
/// Fase D3 — credenciales MinIO propias de Notification (IAM scoped a
/// s3:PutObject en taxvision-temp/notification/*, ver deploy/docker/minio/policies/
/// notification-source.json). Nunca las credenciales root de CloudStorage.
/// </summary>
public sealed class NotificationMinioOptions
{
    public const string SectionName = "Notification:Minio";

    public string Endpoint { get; set; } = "localhost:9000";
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public bool UseTls { get; set; }
    public string TempBucket { get; set; } = "taxvision-temp";
    public string SourcePrefix { get; set; } = "notification";
}
