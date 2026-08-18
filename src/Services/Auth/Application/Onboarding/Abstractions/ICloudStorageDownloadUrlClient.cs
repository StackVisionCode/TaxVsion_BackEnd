using BuildingBlocks.Results;

namespace TaxVision.Auth.Application.Onboarding.Abstractions;

/// <summary>PayFlow (Fase 11) — respalda el endpoint mediador de descarga del recibo
/// (<c>GET /onboarding/receipts/{fileId}/download</c>): en cada click, pide a CloudStorage una URL
/// presignada fresca para el <c>fileId</c> guardado y hace 302 redirect. El file fue almacenado bajo
/// <c>PlatformTenant.Id</c> (ver Documents Fase 10), así que ese es el tenant que se usa para el
/// token M2M — no hay tenant real de onboarding todavía.</summary>
public interface ICloudStorageDownloadUrlClient
{
    Task<Result<Uri>> GetDownloadUrlAsync(Guid fileId, CancellationToken ct = default);
}
