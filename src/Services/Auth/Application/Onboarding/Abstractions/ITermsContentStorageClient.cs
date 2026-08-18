using BuildingBlocks.Results;

namespace TaxVision.Auth.Application.Onboarding.Abstractions;

/// <summary>
/// Auditoría (gap MinIO/legal-docs) — reemplaza el <c>ContentUri</c> externo de
/// <c>TermsVersion.Publish</c> por un documento real almacenado en CloudStorage. Mismo patrón
/// D0/D1 que Documents/Scribe: el PUT a MinIO va directo con IAM propia de Auth (nunca las
/// credenciales root de CloudStorage), y el registro/escaneo lo hace CloudStorage de forma
/// asíncrona vía <c>SaveFileRequestedIntegrationEvent</c>. La lectura reusa el mismo patrón M2M
/// que <see cref="ICloudStorageDownloadUrlClient"/> (URL presignada fresca en cada pedido) pero
/// con su propio <c>ClientId</c> — ver <c>TermsContentStorageClient</c>.
/// </summary>
public interface ITermsContentStorageClient
{
    Task<Result> UploadAsync(
        Guid fileId,
        byte[] content,
        string fileName,
        string contentType,
        Guid actorId,
        CancellationToken ct = default
    );

    Task<Result<string>> DownloadTextAsync(Guid fileId, CancellationToken ct = default);
}
