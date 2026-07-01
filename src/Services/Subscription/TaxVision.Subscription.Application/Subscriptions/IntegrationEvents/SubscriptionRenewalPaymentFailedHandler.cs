using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using TaxVision.Subscription.Application.Abstractions;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

/// <summary>
/// Marks the subscription PastDue when a renewal payment fails.
/// Google Workspace model: access continues until period end — no immediate cutoff.
/// Payment Service handles retry logic independently.
/// </summary>
public static class SubscriptionRenewalPaymentFailedHandler
{
    public static async Task Handle(
        SubscriptionRenewalPaymentFailedIntegrationEvent evt,
        ISubscriptionRepository repo,
        IUnitOfWork uow,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        var subscription = await repo.GetByIdAsync(evt.SubscriptionId, ct);
        if (subscription is null) return;

        var result = subscription.MarkPastDue();
        if (result.IsFailure) return; // already cancelled — idempotente

        await bus.PublishAsync(new TenantEntitlementsChangedIntegrationEvent
        {
            TenantId = subscription.TenantId,
            SubscriptionId = subscription.Id,
            TotalAvailableSeats = subscription.TotalAvailableSeats,
            CorrelationId = correlation.CorrelationId
        });

        await uow.SaveChangesAsync(ct);
    }
}
