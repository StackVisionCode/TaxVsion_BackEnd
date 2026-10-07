using BuildingBlocks.Common;
using BuildingBlocks.Messaging.WalletIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ChargeSaaSPayment;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentApp.Application.SaaSPayments.IntegrationEvents;

/// <summary>
/// Consume el intent de recarga de monedero publicado por <c>TaxVision.Wallet</c> y lo traduce a un
/// <see cref="ChargeSaaSPaymentCommand"/> (cobro off-session Stripe contra la tarjeta guardada). Mismo
/// mecanismo que <see cref="SubscriptionPlanChangeDueConsumer"/>:
/// <see cref="ChargeSaaSPaymentCommand.TargetAggregateId"/> = <c>TopUpId</c>, para que
/// <c>SaaSPaymentResultPublisher.PublishWalletTopUpResultAsync</c> lo devuelva en el round-trip.
/// </summary>
public static class WalletTopUpDueConsumer
{
    public static async Task Handle(
        WalletTopUpDueIntegrationEvent evt,
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
                Type: SaaSPaymentType.WalletTopUp,
                TargetAggregateId: evt.TopUpId,
                Provider: PaymentProviderCode.Stripe,
                PayerEmail: SyntheticPayer.EmailFor(evt.TenantId),
                PayerName: null,
                RequestedByUserId: evt.RequestedByUserId
            );

            var result = await bus.InvokeAsync<Result<Guid>>(command, ct);
            if (result.IsFailure)
            {
                logger.LogError(
                    "ChargeSaaSPayment failed for wallet top-up {TopUpId}: {ErrorCode} — {ErrorMessage}",
                    evt.TopUpId,
                    result.Error.Code,
                    result.Error.Message
                );
            }
        }
    }
}
