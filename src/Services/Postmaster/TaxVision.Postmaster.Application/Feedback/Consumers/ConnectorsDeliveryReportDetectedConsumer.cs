using System.Text.Json;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Feedback.Commands.ProcessDeliveryFeedback;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Application.Suppression;
using TaxVision.Postmaster.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Postmaster.Application.Feedback.Consumers;

/// <summary>
/// Un aviso de no entrega que llegó al buzón de una oficina. Entra por el mismo camino que los avisos
/// del proveedor (traductor + aplicación idempotente), pero solo puede tocar correos de esa oficina.
/// </summary>
public static class ConnectorsDeliveryReportDetectedConsumer
{
    public static async Task Handle(
        ConnectorsDeliveryReportDetectedIntegrationEvent evt,
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
                new ProcessDeliveryFeedbackCommand(
                    FeedbackSource.Dsn.Value,
                    JsonSerializer.Serialize(evt),
                    DateTime.UtcNow,
                    ExpectedTenantId: evt.TenantId
                ),
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
                    "Delivery report discarded: {Code} {Message}",
                    result.Error.Code,
                    result.Error.Message
                );
        }
    }
}
