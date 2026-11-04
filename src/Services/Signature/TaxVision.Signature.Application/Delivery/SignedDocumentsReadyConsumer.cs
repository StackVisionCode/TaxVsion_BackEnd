using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Delivery;

/// <summary>
/// Cuando un archivo final de una solicitud (documento sellado o certificado) queda DISPONIBLE en
/// CloudStorage, revisa si ya están todos los que se van a entregar y, entonces, emite una entrega por
/// firmante con TODOS sus archivos: un solo correo aunque la solicitud tenga varios documentos y
/// certificados. Idempotente: una entrega por firmante; un evento repetido o concurrente no duplica.
/// </summary>
public static class SignedDocumentsReadyConsumer
{
    public static async Task Handle(
        FileAvailableIntegrationEvent evt,
        ISignatureRequestRepository repository,
        DeliverableFiles deliverableFiles,
        ISignedDocumentDeliveryIssuer issuer,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SignatureRequest> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var request =
                await repository.GetBySealedFileIdAsync(evt.TenantId, evt.FileId, ct)
                ?? await repository.GetByCertificateFileIdAsync(evt.TenantId, evt.FileId, ct);
            if (request is null)
                return; // el archivo disponible no es un archivo final de ninguna solicitud

            if (!DeliverableFiles.HasSomethingToDeliver(request))
                return; // la solicitud no pidió entregar nada a los firmantes

            if (!await deliverableFiles.AllAvailableAsync(request, evt.FileId, ct))
                return; // falta alguno: lo dispara el FileAvailable del último

            var issued = await issuer.PrepareCompletedAsync(request, ct);
            if (issued.Count == 0)
                return;

            try
            {
                await unitOfWork.SaveChangesAsync(ct);
            }
            catch (ConflictException)
            {
                // Otro FileAvailable de la misma solicitud ganó la carrera y ya emitió las entregas.
                logger.LogInformation("Deliveries for request {RequestId} were issued concurrently.", request.Id);
                return;
            }

            foreach (var delivery in issued)
                await issuer.PublishAsync(delivery, request, ct);

            logger.LogInformation(
                "Issued {Count} signed-document deliveries for request {RequestId}.",
                issued.Count,
                request.Id
            );
        }
    }
}
