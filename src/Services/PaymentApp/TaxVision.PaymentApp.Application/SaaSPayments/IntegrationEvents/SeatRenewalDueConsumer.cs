using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ChargeSaaSPayment;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentApp.Application.SaaSPayments.IntegrationEvents;

/// <summary>
/// Consume el intent de renovación de un seat y lo traduce a un
/// <see cref="ChargeSaaSPaymentCommand"/>. Independiente de
/// <see cref="SubscriptionRenewalDueConsumer"/> — un seat se cobra y renueva sin afectar la
/// suscripción base ni otros seats.
/// </summary>
public static class SeatRenewalDueConsumer
{
    public static async Task Handle(
        SeatRenewalDueIntegrationEvent evt,
        IMessageBus bus,
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
            var command = new ChargeSaaSPaymentCommand(
                TenantId: evt.TenantId,
                IdempotencyKey: evt.IdempotencyKey,
                AmountCents: evt.AmountCents,
                Currency: evt.Currency,
                Type: SaaSPaymentType.SeatRenewal,
                TargetAggregateId: evt.SeatId,
                Provider: PaymentProviderCode.Stripe,
                PayerEmail: SyntheticPayer.EmailFor(evt.TenantId),
                PayerName: null,
                RequestedByUserId: Guid.Empty
            );

            var result = await bus.InvokeAsync<Result<Guid>>(command, ct);
            if (result.IsFailure)
            {
                logger.LogError(
                    "ChargeSaaSPayment failed for renewal of seat {SeatId}: {ErrorCode} — {ErrorMessage}",
                    evt.SeatId,
                    result.Error.Code,
                    result.Error.Message
                );
            }
        }
    }
}
