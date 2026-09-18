using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Seats;

namespace TaxVision.Subscription.Application.Seats.IntegrationEvents;

/// <summary>El pago por checkout de asientos falló/expiró: marca la <see cref="SeatPurchaseIntent"/> como
/// fallida (no aprovisiona). Idempotente por el estado de la intención.</summary>
public static class SeatsCheckoutFailedConsumer
{
    public static async Task Handle(
        SeatsCheckoutFailedIntegrationEvent evt,
        ISeatPurchaseIntentRepository intents,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SeatPurchaseIntent> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var intent = await intents.GetByIdForProvisioningAsync(evt.SeatPurchaseIntentId, ct);
            if (intent is null)
            {
                logger.LogWarning("SeatsCheckoutFailed for unknown intent {IntentId}.", evt.SeatPurchaseIntentId);
                return;
            }

            if (intent.TenantId != evt.TenantId)
            {
                logger.LogWarning(
                    "SeatsCheckoutFailed tenant mismatch for intent {IntentId}.",
                    evt.SeatPurchaseIntentId
                );
                return;
            }

            var failed = intent.MarkFailed($"{evt.FailureCode}: {evt.FailureReason}", DateTime.UtcNow);
            if (failed.IsFailure)
            {
                logger.LogWarning("Cannot mark intent {IntentId} failed: {Code}.", intent.Id, failed.Error.Code);
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
