using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

/// <summary>PayFlow (Fase 17) — compensa un onboarding cancelado (admin cancel-and-refund, Auth)
/// que ya había activado una suscripción. No-op si el paso Subscription nunca llegó a correr
/// (<see cref="OnboardingCancelRequestedIntegrationEvent.OnboardingSubscriptionId"/> null) — no
/// hay nada que cancelar.</summary>
public static class OnboardingCancelRequestedConsumer
{
    public static async Task Handle(
        OnboardingCancelRequestedIntegrationEvent evt,
        ISubscriptionRepository subscriptions,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantSubscription> logger,
        CancellationToken ct
    )
    {
        if (evt.OnboardingSubscriptionId is null)
            return;

        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            var subscription = await subscriptions.GetByOnboardingIdAsync(evt.OnboardingId, ct);
            if (subscription is null)
            {
                logger.LogWarning(
                    "OnboardingCancelRequested: no subscription found for onboarding {OnboardingId} (expected {SubscriptionId}).",
                    evt.OnboardingId,
                    evt.OnboardingSubscriptionId
                );
                return;
            }

            var result = subscription.CancelImmediately(
                $"Onboarding cancelled and refunded: {evt.Reason}",
                Guid.Empty,
                DateTime.UtcNow
            );
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "OnboardingCancelRequested: could not cancel subscription {SubscriptionId} for onboarding {OnboardingId}: {Code}.",
                    subscription.Id,
                    evt.OnboardingId,
                    result.Error.Code
                );
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation(
                "OnboardingCancelRequested: subscription {SubscriptionId} for onboarding {OnboardingId} cancelled.",
                subscription.Id,
                evt.OnboardingId
            );
        }
    }
}
