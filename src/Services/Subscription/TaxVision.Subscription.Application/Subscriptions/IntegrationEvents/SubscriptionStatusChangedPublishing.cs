using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using TaxVision.Subscription.Domain.Subscriptions;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

/// <summary>
/// Punto único de publicación de <see cref="TenantSubscriptionStatusChangedIntegrationEvent"/> (plan de
/// Expiración/Dunning, Fase 0). Se llama en cada sitio que transiciona el estado del agregado
/// <c>TenantSubscription</c>, JUNTO al recálculo de entitlements existente (aditivo). El caller captura el
/// <paramref name="previousStatus"/> ANTES de la transición y pasa el motivo; el resto sale del agregado.
/// Publish-before-save vía outbox (igual que el recalc): sale sólo si el commit del handler prospera.
/// </summary>
public static class SubscriptionStatusChangedPublishing
{
    public static Task PublishStatusChangedAsync(
        this IMessageBus bus,
        TenantSubscription subscription,
        SubscriptionStatus previousStatus,
        SubscriptionChangeReason reason,
        Guid? actorUserId = null,
        string? failureCode = null,
        string? correlationId = null
    ) =>
        bus.PublishAsync(
                new TenantSubscriptionStatusChangedIntegrationEvent
                {
                    TenantId = subscription.TenantId,
                    TenantSubscriptionId = subscription.Id,
                    Status = subscription.Status.ToString(),
                    PreviousStatus = previousStatus.ToString(),
                    Reason = reason.ToString(),
                    GracePeriodEndsAtUtc =
                        subscription.Status == SubscriptionStatus.GracePeriod
                            ? subscription.GracePeriodEndsAtUtc
                            : null,
                    FailureCode = failureCode,
                    ActorUserId = actorUserId,
                    CorrelationId = correlationId ?? string.Empty,
                }
            )
            .AsTask();
}
