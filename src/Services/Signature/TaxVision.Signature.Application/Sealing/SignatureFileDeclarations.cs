using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Sealing;

/// <summary>
/// Declaraciones de origen de los archivos de una solicitud, compartidas por el backfill manual y el reclamo de
/// CloudStorage: sellados, certificados (o maestros) y copias de las completadas, y originales vigentes de todas.
/// </summary>
public static class SignatureFileDeclarations
{
    /// <summary>Misma condición que la consulta del backfill: completada y con algún sellado.</summary>
    public static bool HasSealedFiles(SignatureRequest request) =>
        request.Status == SignatureRequestStatus.Completed
        && request.Documents.Any(document => document.SealedFileId != null);

    public static RequestOriginalFiles OriginalsOf(SignatureRequest request) =>
        new(
            request.Id,
            [.. request.Documents.Select(document => document.OriginalFileId)],
            [.. request.Signers.Select(signer => signer.MappedCustomerId)]
        );

    public static IEnumerable<FileSourceDeclaredIntegrationEvent> Originals(
        Guid tenantId,
        RequestOriginalFiles request,
        IReadOnlySet<Guid> templateFiles,
        string correlationId
    )
    {
        // Solo los vigentes; el PDF base de una plantilla no se declara (E-9).
        var source = Requests.OriginalFileSources.Of(request.RequestId, request.MappedCustomerIds);
        return request
            .FileIds.Where(fileId => !templateFiles.Contains(fileId))
            .Select(fileId => source.ToDeclaration(tenantId, fileId, correlationId));
    }

    public static IEnumerable<FileSourceDeclaredIntegrationEvent> SealedOutputs(
        SignatureRequest request,
        string correlationId
    )
    {
        var sealedFiles = request
            .Documents.Select(document => document.SealedFileId)
            .OfType<Guid>()
            .Select(fileId => (FileId: fileId, Source: SealedFileRole.For(request, SealedFileRole.Sealed)));
        var certificates = request
            .Documents.Select(document => document.CertificateFileId)
            .Append(request.CertificateFileId)
            .OfType<Guid>()
            .Select(fileId => (FileId: fileId, Source: SealedFileRole.For(request, SealedFileRole.Certificate)));
        var copies = request.CustomerCopies.Select(copy =>
            (
                FileId: copy.CopyFileId,
                Source: new SignatureFileSource(
                    request.Id,
                    SealedFileRole.OfCopy(request, copy.SourceFileId),
                    copy.CustomerId
                )
            )
        );

        return sealedFiles
            .Concat(certificates)
            .Concat(copies)
            .Select(file => file.Source.ToDeclaration(request.TenantId, file.FileId, correlationId));
    }
}
