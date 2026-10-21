using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

/// <summary>Avisa a PaymentApp que esos cobros automáticos ya se pagaron por otro lado y no se reintentan.</summary>
public static class RenewalChargesSupersededPublishing
{
    public static Task PublishRenewalChargesSupersededAsync(
        this IMessageBus bus,
        Guid tenantId,
        IReadOnlyList<string> idempotencyKeys,
        string correlationId
    )
    {
        if (idempotencyKeys.Count == 0)
            return Task.CompletedTask;

        return bus.PublishAsync(
                new RenewalChargesSupersededIntegrationEvent
                {
                    TenantId = tenantId,
                    IdempotencyKeys = idempotencyKeys,
                    CorrelationId = correlationId,
                }
            )
            .AsTask();
    }
}
