using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Application.Ingest;

/// <summary>
/// Consume <c>connectors.raw_message_received.v1</c> (Connectors Fase 7/8). El correo de un cliente entra a su ficha;
/// el de quien todavía no es cliente entra sin cliente a la vista "Not a client" (D-COR-3) y se le enlaza cuando el
/// cliente aparece (<see cref="LinkUnlinkedMailHandler"/>). Fetch de body (Fase 5) y descarga de attachments
/// (Fase 8/12) están explícitamente fuera de alcance de este consumer.
/// </summary>
public static class RawMessageReceivedConsumer
{
    public static async Task Handle(
        ConnectorsRawMessageReceivedIntegrationEvent evt,
        IncomingMailIngestor ingestor,
        IncomingSenderResolver senders,
        IngestRejectionRecorder rejections,
        ICorrelationContext correlation,
        ILogger<IncomingEmail> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            var message = IncomingMessageNormalizer.Normalize(evt);
            if (await ingestor.TryRecordDuplicateAsync(evt, message, ct))
                return;

            var fromResult = EmailAddress.Create(evt.From);
            if (fromResult.IsFailure)
            {
                logger.LogWarning(
                    "raw_message_received event {EventId} has an invalid From address; recorded as InvalidSender.",
                    evt.EventId
                );
                await rejections.RejectInvalidSenderAsync(evt, message, fromResult.Error.Code, ct);
                return;
            }
            var from = fromResult.Value;

            var sender = await senders.ResolveAsync(evt.TenantId, from, ct);
            if (sender.IsIgnoredSystemSender)
            {
                logger.LogDebug("System sender for event {EventId} is not a customer; skipped.", evt.EventId);
                return;
            }

            // Transparencia: un correo que falla autenticación NO se pone en cuarentena oculta. Entra al
            // inbox marcado (SenderAuthentication → veredicto Unverified) para que el usuario lo vea y decida.
            await ingestor.IngestAsync(evt, message, sender.CustomerId, from, ct);
        }
    }
}
