using System.Globalization;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using TaxVision.Subscription.Domain.Subscriptions.Events;
using TaxVision.Subscription.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.DomainEvents;

/// <summary>
/// El aviso al cliente de un cambio de ciclo. Viaja por el canal de estado con el motivo BillingCycleChanged (no es una
/// transición) para reusar la resolución del destinatario en Auth y el envío en Notification. Cascada por el outbox.
/// </summary>
public static class SubscriptionBillingCycleChangedDomainEventHandler
{
    private static readonly CultureInfo English = new("en-US");

    public static TenantSubscriptionStatusChangedIntegrationEvent Handle(
        SubscriptionBillingCycleChangedDomainEvent changed,
        Envelope envelope
    ) =>
        new()
        {
            TenantId = changed.TenantId,
            TenantSubscriptionId = changed.TenantSubscriptionId,
            Status = changed.Status.ToString(),
            PreviousStatus = changed.Status.ToString(),
            Reason = nameof(SubscriptionChangeReason.BillingCycleChanged),
            ActorUserId = changed.ActorUserId,
            CorrelationId = envelope.CorrelationId ?? string.Empty,
            Amount = changed.ChargedAmountCents is { } cents
                ? (cents / 100m).ToString("F2", CultureInfo.InvariantCulture)
                : null,
            Cadence = changed.NewBillingCycle == BillingCycle.Yearly ? "year" : "month",
            // Con cobro: cuándo se cobró. Sin cobro (paso a mensual agendado): vacío, y la fecha va en la próxima.
            DateCharged = changed.ChargedAmountCents is null
                ? null
                : changed.EffectiveAtUtc.ToString("MMMM d, yyyy", English),
            NextChargeDate = changed.NextRenewalAtUtc.ToString("MMMM d, yyyy", English),
        };
}
