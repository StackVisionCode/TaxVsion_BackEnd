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
/// Consume el intent de cobro de un upgrade de plan publicado por Subscription y lo traduce a
/// un <see cref="ChargeSaaSPaymentCommand"/> — mismo mecanismo que
/// <see cref="SubscriptionRenewalDueConsumer"/>, pero <see cref="ChargeSaaSPaymentCommand.TargetAggregateId"/>
/// es el <c>PlanChangeRequestId</c> (no el TenantSubscriptionId): es lo único que Subscription
/// necesita para ubicar el request de vuelta, y así <see cref="SaaSPaymentChargeOutcome"/> no
/// necesita ningún campo nuevo para el round-trip.
/// </summary>
public static class SubscriptionPlanChangeDueConsumer
{
    public static async Task Handle(
        SubscriptionPlanChangeDueIntegrationEvent evt,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        CancellationToken ct)
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var command = new ChargeSaaSPaymentCommand(
                TenantId: evt.TenantId,
                IdempotencyKey: evt.IdempotencyKey,
                AmountCents: evt.TargetPlanPrice,
                Currency: evt.Currency,
                Type: SaaSPaymentType.PlanChangeCharge,
                TargetAggregateId: evt.PlanChangeRequestId,
                Provider: PaymentProviderCode.Stripe,
                PayerEmail: SyntheticPayer.EmailFor(evt.TenantId),
                PayerName: null,
                RequestedByUserId: Guid.Empty);

            var result = await bus.InvokeAsync<Result<Guid>>(command, ct);
            if (result.IsFailure)
            {
                logger.LogError(
                    "ChargeSaaSPayment failed for plan change request {PlanChangeRequestId}: {ErrorCode} — {ErrorMessage}",
                    evt.PlanChangeRequestId, result.Error.Code, result.Error.Message);
            }
        }
    }
}
