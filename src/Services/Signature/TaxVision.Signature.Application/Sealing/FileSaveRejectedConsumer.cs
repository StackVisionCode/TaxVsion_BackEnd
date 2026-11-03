using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Domain.Requests;
using Wolverine;

namespace TaxVision.Signature.Application.Sealing;

/// <summary>
/// CloudStorage descartó un archivo que Signature pidió guardar: ese FileId nunca existirá. Si es un
/// archivo final del sellado (documento sellado o certificado), la solicitud suelta la referencia
/// (<see cref="SignatureRequest.ReleaseRejectedArtifact"/>) para no ofrecer una descarga inexistente, y
/// se publica <see cref="SignatureRequestSealingFailedIntegrationEvent"/>, el aviso de sellado fallido
/// que ya existe. El pipeline de sellado es re-entrante (sella lo que no tenga SealedFileId y genera el
/// certificado que falte) y lo regenera cuando se vuelve a disparar: el Completed de una solicitud aún
/// en curso, o un re-encolado manual.
///
/// <para>
/// No se re-dispara el sellado desde aquí: la mayoría de los rechazos son permanentes (cuota, política
/// de subida, dueño inválido) y re-sellar en automático sería un bucle. Tampoco se agrega a la cadena de
/// auditoría: el <c>RequestSealed</c> ya registrado describe un sellado que sí se produjo, y el nuevo
/// sellado agrega el suyo. Para imágenes de firma y copias parciales basta la proyección local, que
/// <c>FileSaveRejectedProjectionConsumer</c> deja en Rejected: el sellado cae al sello tipográfico y la
/// copia parcial falla en vez de entregarse.
/// </para>
/// </summary>
public static class FileSaveRejectedConsumer
{
    private static readonly TimeSpan LockTtl = TimeSpan.FromMinutes(1);
    private const string SignaturesFolder = "Signatures";

    public static async Task Handle(
        FileSaveRejectedIntegrationEvent evt,
        ISignatureRequestRepository repository,
        IDistributedLock distributedLock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<SignatureRequest> logger,
        CancellationToken ct
    )
    {
        if (!string.Equals(evt.RequestingService, SignatureStorage.RequestingService, StringComparison.Ordinal))
            return;

        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var request =
                await repository.GetBySealedFileIdAsync(evt.TenantId, evt.FileId, ct)
                ?? await repository.GetByCertificateFileIdAsync(evt.TenantId, evt.FileId, ct);
            if (request is null)
            {
                // Imagen de firma, firma de perfil, copia parcial (las cubre la proyección) o una referencia ya soltada.
                logger.LogWarning(
                    "CloudStorage rejected Signature file {FileId} ({Reason}); no sealed document or certificate references it.",
                    evt.FileId,
                    evt.Reason
                );
                return;
            }

            // Mismo lock que el sellado: que no grabe un certificado sobre el documento que se suelta.
            await using var lockHandle = await distributedLock.AcquireAsync(
                SignatureRequestCompletedConsumer.SealingLockKey(request.Id),
                LockTtl,
                ct
            );
            if (!lockHandle.IsAcquired)
                throw new InvalidOperationException(
                    $"Sealing of request {request.Id} is in progress; the rejection of file {evt.FileId} will be retried."
                );

            var released = request.ReleaseRejectedArtifact(evt.FileId);
            if (released is null)
                return;

            await unitOfWork.SaveChangesAsync(ct);

            logger.LogWarning(
                "CloudStorage rejected {Kind} {FileId} of request {RequestId} ({Reason}); the reference was released and the request needs sealing again.",
                released.Kind,
                evt.FileId,
                request.Id,
                evt.Reason
            );

            await bus.PublishAsync(
                new SignatureRequestSealingFailedIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = correlationId,
                    SignatureRequestId = request.Id,
                    CreatedByUserId = request.CreatedByUserId,
                    Reason = $"Storage rejected the {Describe(released.Kind)} ({evt.Reason}). It must be sealed again.",
                    ErrorCode = "Signature.Storage.SaveRejected",
                    FailedAtUtc = DateTime.UtcNow,
                }
            );

            await RequestDetachedCertificatesDeletionAsync(request, released, correlationId, bus, logger);
        }
    }

    /// <summary>
    /// Los certificados desligados existen pero atestiguan el hash de un sellado que nunca existirá: se piden
    /// borrar (papelera de CloudStorage, con las copias de portal que se hicieron de ellos) por el mecanismo
    /// genérico, que verifica el dueño actual y salta lo que no coincida o tenga legal hold. Una solicitud con
    /// legal hold no pierde nada. Se publica después de guardar (outbox) y un replay ya no suelta nada.
    /// </summary>
    private static async Task RequestDetachedCertificatesDeletionAsync(
        SignatureRequest request,
        SealingArtifactRelease released,
        string correlationId,
        IMessageBus bus,
        ILogger logger
    )
    {
        if (released.DetachedCertificateFileIds.Count == 0)
            return;

        if (request.LegalHold)
        {
            logger.LogWarning(
                "Request {RequestId} is under legal hold; its detached certificates {FileIds} are kept.",
                request.Id,
                released.DetachedCertificateFileIds
            );
            return;
        }

        await bus.PublishAsync(
            DetachedCertificatesDeletion(request, released.DetachedCertificateFileIds, correlationId)
        );
    }

    private static FilesDeletionRequestedIntegrationEvent DetachedCertificatesDeletion(
        SignatureRequest request,
        IReadOnlyList<Guid> certificateFileIds,
        string correlationId
    )
    {
        // Mismo dueño y carpeta con que se subieron (ver BuildCertificateUpload del sellado).
        var (ownerType, ownerId) = SealedDocumentOwner.Resolve(
            request.Signers.Select(signer => signer.MappedCustomerId).ToList(),
            request.Id
        );
        var certificates = certificateFileIds.Select(fileId => new FileDeletionTarget
        {
            FileId = fileId,
            ExpectedOwnerType = ownerType,
            ExpectedOwnerId = ownerId,
            ExpectedFolderType = SignaturesFolder,
        });
        var portalCopies = request
            .CustomerCopies.Where(copy => certificateFileIds.Contains(copy.SourceFileId))
            .Select(copy => new FileDeletionTarget
            {
                FileId = copy.CopyFileId,
                ExpectedOwnerType = "Customer",
                ExpectedOwnerId = copy.CustomerId,
                ExpectedFolderType = SignaturesFolder,
            });

        return new FilesDeletionRequestedIntegrationEvent
        {
            TenantId = request.TenantId,
            CorrelationId = correlationId,
            SourceService = SignatureStorage.RequestingService,
            SourceReferenceId = request.Id,
            Reason = "sealed_document_rejected",
            Files = certificates.Concat(portalCopies).ToList(),
        };
    }

    private static string Describe(SealingArtifactKind kind) =>
        kind switch
        {
            SealingArtifactKind.SealedDocument => "signed document",
            SealingArtifactKind.RequestCertificate => "certificate of completion",
            _ => "document certificate",
        };
}
