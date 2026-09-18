using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.Seats;
using Wolverine;

namespace TaxVision.Subscription.Application.Seats.IntegrationEvents;

/// <summary>
/// Cierra el flujo de compra de asientos por HOSTED-CHECKOUT: al confirmarse el pago, aprovisiona los
/// asientos de la <see cref="SeatPurchaseIntent"/> (crea + activa, co-terminados al período de la base) SIN
/// volver a cobrar — el checkout ya cobró. Idempotente: todo en un único <c>SaveChangesAsync</c> y la
/// intención pasa a <c>Provisioned</c>; una redelivery la encuentra ya aprovisionada y no crea duplicados.
/// </summary>
public static class SeatsCheckoutPaidConsumer
{
    public static async Task Handle(
        SeatsCheckoutPaidIntegrationEvent evt,
        ISeatPurchaseIntentRepository intents,
        ISubscriptionRepository subscriptions,
        ISubscriptionSeatRepository seats,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ISubscriptionAuditLogWriter audit,
        ISubscriptionMetrics metrics,
        ILogger<SubscriptionSeat> logger,
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
                logger.LogWarning("SeatsCheckoutPaid for unknown intent {IntentId}.", evt.SeatPurchaseIntentId);
                return;
            }

            // IgnoreQueryFilters en el repo + este guard = mismo patrón que los consumers de resultado de pago.
            if (intent.TenantId != evt.TenantId)
            {
                logger.LogWarning("SeatsCheckoutPaid tenant mismatch for intent {IntentId}.", evt.SeatPurchaseIntentId);
                return;
            }

            if (intent.Status == SeatPurchaseIntentStatus.Provisioned)
                return; // ya aprovisionada (redelivery)

            var subscription = await subscriptions.GetByTenantIdAsync(evt.TenantId, ct);
            if (subscription is null)
            {
                logger.LogWarning("SeatsCheckoutPaid for tenant {TenantId} without a subscription.", evt.TenantId);
                return;
            }

            var nowUtc = DateTime.UtcNow;
            var provisioned = await SeatCheckoutProvisioning.TryProvisionAsync(
                intent,
                subscription,
                seats,
                audit,
                metrics,
                correlation.CorrelationId,
                provisioningMethod: "Checkout",
                evt.PaidAtUtc,
                nowUtc,
                ct
            );
            if (!provisioned)
            {
                logger.LogWarning("Cannot provision intent {IntentId} (status {Status}).", intent.Id, intent.Status);
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);

            bus.TenantId = evt.TenantId.ToString();
            await bus.RecalculateEntitlementsSafelyAsync(evt.TenantId, logger, ct);

            logger.LogInformation(
                "Provisioned {Quantity} {SeatType} seat(s) for tenant {TenantId} from paid checkout intent {IntentId}.",
                intent.Quantity,
                intent.SeatType,
                evt.TenantId,
                intent.Id
            );
        }
    }
}
