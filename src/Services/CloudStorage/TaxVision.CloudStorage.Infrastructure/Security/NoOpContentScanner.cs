using TaxVision.CloudStorage.Application.Abstractions;

namespace TaxVision.CloudStorage.Infrastructure.Security;

/// <summary>
/// Implementacion por defecto de <see cref="IContentScanner"/> — siempre Clean.
/// Reemplazar con un scanner real (NSFW/CSAM) via DI cuando exista uno; el
/// pipeline en <c>ScanFileHandler</c> ya esta listo para los otros verdicts.
/// </summary>
public sealed class NoOpContentScanner : IContentScanner
{
    public Task<ContentScanResult> ScanAsync(Stream content, ContentScanContext context, CancellationToken ct) =>
        Task.FromResult(ContentScanResult.Clean());
}
