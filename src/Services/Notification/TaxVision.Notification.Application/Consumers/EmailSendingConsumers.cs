using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>
/// Consume la solicitud de entrega (durable inbox) y delega en el servicio de entrega. Se ejecuta
/// fuera del request HTTP; el envío masivo/asíncrono nunca ocurre en el controller.
/// </summary>
public static class EmailSendRequestedConsumer
{
    public static async Task Handle(
        EmailSendRequestedIntegrationEvent evt,
        IEmailDeliveryService delivery,
        ICorrelationContext correlation,
        ILogger<EmailSendRequestedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            var result = await delivery.DeliverAsync(evt.MessageId, ct);
            if (result.IsFailure)
                logger.LogWarning(
                    "Email delivery could not start for {MessageId}: {Error}",
                    evt.MessageId,
                    result.Error.Message
                );
        }
    }
}
