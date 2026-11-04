using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Application.Delivery;

/// <summary>
/// Qué archivos finales se entregan a los firmantes (sellados y/o certificados, según la solicitud) y si ya
/// están disponibles. La proyección local manda; si no tiene el archivo (evento perdido o aún no aplicado)
/// se pregunta a CloudStorage, que es la fuente.
/// </summary>
public sealed class DeliverableFiles(IFileMetadataRefRepository files, ISignatureCloudStorageClient storage)
{
    public static bool HasSomethingToDeliver(SignatureRequest request) =>
        request.SendSealedDocumentToSigners || (request.GenerateCertificate && request.SendCertificateToSigners);

    /// <summary>
    /// true si todos los archivos a entregar existen y pasaron el scan. <paramref name="availableFileId"/> es el
    /// del evento en curso: cuenta como disponible aunque su proyección aún no se haya guardado.
    /// </summary>
    public async Task<bool> AllAvailableAsync(SignatureRequest request, Guid? availableFileId, CancellationToken ct)
    {
        if (request.Documents.Any(document => document.SealedFileId is null))
            return false;

        var expected = new List<Guid>();
        if (request.SendSealedDocumentToSigners)
            expected.AddRange(request.Documents.Select(document => document.SealedFileId!.Value));

        if (request.GenerateCertificate && request.SendCertificateToSigners)
        {
            var certificates =
                request.CertificateGenerationMode == CertificateGenerationMode.SingleForRequest
                    ? new[] { request.CertificateFileId }
                    : request.Documents.Select(document => document.CertificateFileId).ToArray();
            if (certificates.Any(certificate => certificate is null))
                return false; // aún no se registraron
            expected.AddRange(certificates.Select(certificate => certificate!.Value));
        }

        foreach (var fileId in expected.Distinct().Where(fileId => fileId != availableFileId))
        {
            if (!await IsAvailableAsync(request.TenantId, fileId, ct))
                return false;
        }
        return true;
    }

    private async Task<bool> IsAvailableAsync(Guid tenantId, Guid fileId, CancellationToken ct)
    {
        var projection = await files.GetByFileIdAsync(tenantId, fileId, ct);
        if (projection is not null)
            return projection.Status == FileScanStatus.Available;

        var remote = await storage.GetFileAsync(tenantId, fileId, ct);
        return remote.IsSuccess && string.Equals(remote.Value.Status, "Available", StringComparison.OrdinalIgnoreCase);
    }
}
