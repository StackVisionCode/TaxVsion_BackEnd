using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;
using Wolverine;

namespace TaxVision.Signature.Application.Delivery;

/// <summary>
/// Cuando un archivo final de una solicitud (documento sellado o certificado) queda DISPONIBLE en
/// CloudStorage, revisa si ya están todos los que se van a entregar y, entonces, emite una entrega por
/// firmante con TODOS sus archivos: un solo correo aunque la solicitud tenga varios documentos y
/// certificados. Idempotente: una entrega por firmante; un evento repetido no duplica y uno concurrente espera
/// el candado de <see cref="IDeliveryIssuanceLock"/>. No se atrapa el choque con el índice único: el guardado
/// final de Wolverine repetiría el mismo insert.
/// </summary>
public static class SignedDocumentsReadyConsumer
{
    public static async Task Handle(
        FileAvailableIntegrationEvent evt,
        ISignatureRequestRepository repository,
        DeliverableFiles deliverableFiles,
        ISignedDocumentDeliveryIssuer issuer,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
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

            await unitOfWork.SaveChangesAsync(ct);

            foreach (var delivery in issued)
                await issuer.PublishAsync(delivery, request, bus, ct);

            logger.LogInformation(
                "Issued {Count} signed-document deliveries for request {RequestId}.",
                issued.Count,
                request.Id
            );
        }
    }
}
