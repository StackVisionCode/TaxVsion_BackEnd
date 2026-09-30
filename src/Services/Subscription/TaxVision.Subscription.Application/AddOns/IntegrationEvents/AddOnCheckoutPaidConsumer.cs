using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.AddOns;
using Wolverine;

namespace TaxVision.Subscription.Application.AddOns.IntegrationEvents;

/// <summary>El checkout de un add-on se pagó: recién acá nace el <c>TenantAddOn</c>. Molde:
/// <c>SeatsCheckoutPaidConsumer</c>.</summary>
public static class AddOnCheckoutPaidConsumer
{
    public static async Task Handle(
        AddOnCheckoutPaidIntegrationEvent evt,
        IAddOnPurchaseIntentRepository intents,
        ISubscriptionRepository subscriptions,
        IAddOnDefinitionRepository addOnDefinitions,
        ITenantAddOnRepository tenantAddOns,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ISubscriptionAuditLogWriter audit,
        ISubscriptionMetrics metrics,
        ILogger<TenantAddOn> logger,
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
                logger.LogWarning("AddOnCheckoutPaid for unknown intent {IntentId}.", evt.AddOnPurchaseIntentId);
                return;
            }

            if (intent.TenantId != evt.TenantId)
            {
                logger.LogWarning("AddOnCheckoutPaid tenant mismatch for intent {IntentId}.", intent.Id);
                return;
            }

            if (intent.Status == AddOnPurchaseIntentStatus.Provisioned)
                return; // ya activado (redelivery)

            var subscription = await subscriptions.GetByTenantIdAsync(evt.TenantId, ct);
            if (subscription is null)
            {
                logger.LogWarning("AddOnCheckoutPaid for tenant {TenantId} without a subscription.", evt.TenantId);
                return;
            }

            var definition = await addOnDefinitions.GetByIdAsync(intent.AddOnDefinitionId, ct);
            if (definition is null)
            {
                logger.LogWarning("AddOnCheckoutPaid for intent {IntentId} with an unknown add-on.", intent.Id);
                return;
            }

            var nowUtc = DateTime.UtcNow;
            var addOn = await AddOnCheckoutProvisioning.TryProvisionAsync(
                intent,
                subscription,
                definition,
                tenantAddOns,
                audit,
                metrics,
                correlation.CorrelationId,
                provisioningMethod: "Checkout",
                evt.PaidAtUtc,
                nowUtc,
                ct
            );
            if (addOn is null)
            {
                logger.LogWarning(
                    "Cannot provision add-on intent {IntentId} (status {Status}).",
                    intent.Id,
                    intent.Status
                );
                return;
            }

            await bus.PublishAsync(
                new AddOnActivatedIntegrationEvent
                {
                    TenantId = intent.TenantId,
                    TenantAddOnId = addOn.Id,
                    AddOnCode = addOn.AddOnCode,
                    Quantity = addOn.Quantity,
                    CurrentPeriodEndUtc = addOn.CurrentPeriodEndUtc,
                    CorrelationId = correlation.CorrelationId,
                }
            );
            await unitOfWork.SaveChangesAsync(ct);

            bus.TenantId = evt.TenantId.ToString();
            await bus.RecalculateEntitlementsSafelyAsync(evt.TenantId, logger, ct);

            logger.LogInformation(
                "Activated add-on {AddOnCode} for tenant {TenantId} from paid checkout intent {IntentId}.",
                addOn.AddOnCode,
                evt.TenantId,
                intent.Id
            );
        }
    }
}
