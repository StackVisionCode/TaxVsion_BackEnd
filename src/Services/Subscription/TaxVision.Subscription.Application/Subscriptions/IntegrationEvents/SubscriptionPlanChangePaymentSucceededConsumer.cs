using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.Subscriptions;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

/// <summary>Cierra el loop de upgrade: PaymentApp confirmó el cobro del precio completo, así
/// que recién ACÁ se aplica el cambio de plan (y se reinicia el ciclo de facturación) — antes
/// de este consumer, PlanId/PlanVersionId no se habían tocado (ver
/// TenantSubscription.RequestUpgrade).</summary>
public static class SubscriptionPlanChangePaymentSucceededConsumer
{
    public static async Task Handle(
        SubscriptionPlanChangePaymentSucceededIntegrationEvent evt,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<TenantSubscription> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var subscription = await subscriptions.GetByTenantIdAsync(evt.TenantId, ct);
            if (subscription is null)
            {
                logger.LogWarning("SubscriptionPlanChangePaymentSucceeded for unknown tenant {TenantId}.", evt.TenantId);
                return;
            }

            var request = FindRequestById(subscription, evt.PlanChangeRequestId);
            if (request is null)
            {
                logger.LogWarning(
                    "SubscriptionPlanChangePaymentSucceeded for tenant {TenantId} has no matching PlanChangeRequest {PlanChangeRequestId}.",
                    evt.TenantId, evt.PlanChangeRequestId);
                return;
            }

            var toPlan = await plans.GetByIdAsync(request.ToPlanId, ct);
            var toPlanVersion = toPlan?.Versions.FirstOrDefault(v => v.Id == request.ToPlanVersionId);
            if (toPlan is null || toPlanVersion is null)
            {
                logger.LogWarning(
                    "Could not apply plan change {PlanChangeRequestId} for subscription {SubscriptionId}: target plan/version no longer exists.",
                    request.Id, subscription.Id);
                return;
            }

            var result = subscription.CompleteUpgradeCharge(
                request.Id, toPlan, toPlanVersion, evt.SaaSPaymentId, actorUserId: Guid.Empty, evt.PaidAtUtc);
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Could not complete plan change charge for subscription {TenantSubscriptionId}: {Code}.", subscription.Id, result.Error.Code);
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);
            await bus.RecalculateEntitlementsSafelyAsync(subscription.TenantId, logger, ct);
        }
    }

    private static PlanChangeRequest? FindRequestById(TenantSubscription subscription, Guid requestId)
    {
        foreach (var request in subscription.PlanChangeRequests)
        {
            if (request.Id == requestId)
                return request;
        }

        return null;
    }
}
