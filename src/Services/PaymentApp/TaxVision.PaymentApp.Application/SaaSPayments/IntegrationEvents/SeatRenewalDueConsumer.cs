using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Billing.Abstractions;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ChargeSaaSPayment;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using Wolverine;

namespace TaxVision.PaymentApp.Application.SaaSPayments.IntegrationEvents;

/// <summary>
/// Consume el intent de renovación de un seat y lo traduce a un
/// <see cref="ChargeSaaSPaymentCommand"/>. Independiente de
/// <see cref="SubscriptionRenewalDueConsumer"/> — un seat se cobra y renueva sin afectar la
/// suscripción base ni otros seats. El provider sale del <c>TenantBillingPreference</c> del tenant
/// vía <see cref="DunningProviderResolver"/>: antes se hardcodeaba Stripe y los seats de un tenant
/// que onboardeó con PayPal entraban en loop infinito de "Failed — provider declined".
/// </summary>
public static class SeatRenewalDueConsumer
{
    public static async Task Handle(
        SeatRenewalDueIntegrationEvent evt,
        IMessageBus bus,
        ITenantBillingPreferenceProjectionRepository preferences,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var preference = await preferences.GetAsync(evt.TenantId, ct);
            var provider = DunningProviderResolver.Resolve(preference);
            var command = new ChargeSaaSPaymentCommand(
                TenantId: evt.TenantId,
                IdempotencyKey: evt.IdempotencyKey,
                AmountCents: evt.AmountCents,
                Currency: evt.Currency,
                Type: SaaSPaymentType.SeatRenewal,
                TargetAggregateId: evt.SeatId,
                Provider: provider,
                PayerEmail: SyntheticPayer.EmailFor(evt.TenantId),
                PayerName: null,
                RequestedByUserId: Guid.Empty
            );

            var result = await bus.InvokeAsync<Result<Guid>>(command, ct);
            if (result.IsFailure)
            {
                logger.LogError(
                    "ChargeSaaSPayment failed for renewal of seat {SeatId} (provider {Provider}): {ErrorCode} — {ErrorMessage}",
                    evt.SeatId,
                    provider,
                    result.Error.Code,
                    result.Error.Message
                );
            }
        }
    }
}
