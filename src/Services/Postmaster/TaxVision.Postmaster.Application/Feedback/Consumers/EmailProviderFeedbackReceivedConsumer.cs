using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Feedback.Commands.ProcessDeliveryFeedback;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Application.Suppression;
using Wolverine;

namespace TaxVision.Postmaster.Application.Feedback.Consumers;

/// <summary>
/// Aviso del webhook único que Notification reenvió crudo. Un payload ilegible o de un origen sin
/// traductor no se arregla reintentando: se loguea y se descarta.
/// </summary>
public static class EmailProviderFeedbackReceivedConsumer
{
    public static async Task Handle(
        EmailProviderFeedbackReceivedIntegrationEvent evt,
        IDeliveryFeedbackCapabilities capabilities,
        ISentMessageRepository sentMessages,
        IDeliveryFeedbackReceiptRepository receipts,
        IUnmatchedDeliveryFeedbackRepository unmatched,
        ISuppressionListRepository suppressionList,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            var result = await ProcessDeliveryFeedbackHandler.Handle(
                new ProcessDeliveryFeedbackCommand(evt.Source, evt.RawPayload, evt.ReceivedAtUtc),
                capabilities,
                sentMessages,
                receipts,
                unmatched,
                suppressionList,
                unitOfWork,
                bus,
                logger,
                ct
            );

            if (result.IsFailure)
                logger.LogWarning(
                    "Provider feedback from {Source} discarded: {Code} {Message}",
                    evt.Source,
                    result.Error.Code,
                    result.Error.Message
                );
        }
    }
}
