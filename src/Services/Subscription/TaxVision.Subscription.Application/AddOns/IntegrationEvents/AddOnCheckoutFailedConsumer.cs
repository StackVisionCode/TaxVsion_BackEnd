using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.AddOns;

namespace TaxVision.Subscription.Application.AddOns.IntegrationEvents;

/// <summary>El checkout de un add-on falló o se canceló: se cierra la intención. No hay nada que revertir —
/// el <c>TenantAddOn</c> nunca llegó a crearse.</summary>
public static class AddOnCheckoutFailedConsumer
{
    public static async Task Handle(
        AddOnCheckoutFailedIntegrationEvent evt,
        IAddOnPurchaseIntentRepository intents,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<AddOnPurchaseIntent> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var intent = await intents.GetByIdForProvisioningAsync(evt.AddOnPurchaseIntentId, ct);
            if (intent is null)
            {
                logger.LogWarning("AddOnCheckoutFailed for unknown intent {IntentId}.", evt.AddOnPurchaseIntentId);
                return;
            }

            if (intent.TenantId != evt.TenantId)
            {
                logger.LogWarning("AddOnCheckoutFailed tenant mismatch for intent {IntentId}.", intent.Id);
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
