using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

/// <summary>
/// Pide el correo de "no pudimos cobrar" por cada intento automático fallido de una renovación (plan,
/// asiento o add-on). Un checkout hosteado no lo pide: ahí el usuario está pagando en pantalla.
/// </summary>
public static class RenewalChargeFailedPublishing
{
    public static Task PublishRenewalChargeFailedAsync(
        this IMessageBus bus,
        SubscriptionRenewalPaymentFailedIntegrationEvent evt,
        string itemDescription,
        string? consequence,
        string correlationId
    ) =>
        evt.IsAutomaticCharge
            ? PublishAsync(
                bus,
                new RenewalChargeFailedIntegrationEvent
                {
                    TenantId = evt.TenantId,
                    SaaSPaymentId = evt.SaaSPaymentId,
                    ChargeKind = RenewalChargeKinds.Plan,
                    ItemDescription = itemDescription,
                    AttemptNumber = evt.AttemptNumber,
                    MaxAttempts = evt.MaxAttempts,
                    IsFinal = !evt.WillRetry,
                    NextRetryAtUtc = evt.WillRetry ? evt.NextRetryAtUtc : null,
                    AmountCents = evt.AmountCents,
                    Currency = evt.Currency,
                    CardBrand = evt.CardBrand,
                    CardLast4 = evt.CardLast4,
                    FailureCode = evt.FailureCode,
                    Consequence = evt.WillRetry ? null : consequence,
                    CorrelationId = correlationId,
                }
            )
            : Task.CompletedTask;

    public static Task PublishRenewalChargeFailedAsync(
        this IMessageBus bus,
        SeatRenewalPaymentFailedIntegrationEvent evt,
        string? consequence,
        string correlationId
    ) =>
        evt.IsAutomaticCharge
            ? PublishAsync(
                bus,
                new RenewalChargeFailedIntegrationEvent
                {
                    TenantId = evt.TenantId,
                    SaaSPaymentId = evt.SaaSPaymentId,
                    ChargeKind = RenewalChargeKinds.Seat,
                    ItemDescription = RenewalItemDescriptions.Seat,
                    AttemptNumber = evt.AttemptNumber,
                    MaxAttempts = evt.MaxAttempts,
                    IsFinal = !evt.WillRetry,
                    NextRetryAtUtc = evt.WillRetry ? evt.NextRetryAtUtc : null,
                    AmountCents = evt.AmountCents,
                    Currency = evt.Currency,
                    CardBrand = evt.CardBrand,
                    CardLast4 = evt.CardLast4,
                    FailureCode = evt.FailureCode,
                    Consequence = evt.WillRetry ? null : consequence,
                    CorrelationId = correlationId,
                }
            )
            : Task.CompletedTask;

    public static Task PublishRenewalChargeFailedAsync(
        this IMessageBus bus,
        AddOnRenewalPaymentFailedIntegrationEvent evt,
        string itemDescription,
        string? consequence,
        string correlationId
    ) =>
        evt.IsAutomaticCharge
            ? PublishAsync(
                bus,
                new RenewalChargeFailedIntegrationEvent
                {
                    TenantId = evt.TenantId,
                    SaaSPaymentId = evt.SaaSPaymentId,
                    ChargeKind = RenewalChargeKinds.AddOn,
                    ItemDescription = itemDescription,
                    AttemptNumber = evt.AttemptNumber,
                    MaxAttempts = evt.MaxAttempts,
                    IsFinal = !evt.WillRetry,
                    NextRetryAtUtc = evt.WillRetry ? evt.NextRetryAtUtc : null,
                    AmountCents = evt.AmountCents,
                    Currency = evt.Currency,
                    CardBrand = evt.CardBrand,
                    CardLast4 = evt.CardLast4,
                    FailureCode = evt.FailureCode,
                    Consequence = evt.WillRetry ? null : consequence,
                    CorrelationId = correlationId,
                }
            )
            : Task.CompletedTask;

    private static Task PublishAsync(IMessageBus bus, RenewalChargeFailedIntegrationEvent evt)
    {
        bus.TenantId = evt.TenantId.ToString();
        return bus.PublishAsync(evt).AsTask();
    }
}
