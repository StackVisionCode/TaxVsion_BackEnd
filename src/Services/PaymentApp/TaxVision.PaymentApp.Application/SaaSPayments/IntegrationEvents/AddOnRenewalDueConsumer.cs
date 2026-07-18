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
/// Consume el intent de renovación de un add-on y lo traduce a un
/// <see cref="ChargeSaaSPaymentCommand"/>. Independiente de la suscripción base y de los
/// seats.
/// </summary>
public static class AddOnRenewalDueConsumer
{
    public static async Task Handle(
        AddOnRenewalDueIntegrationEvent evt,
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
                Type: SaaSPaymentType.AddOnRenewal,
                TargetAggregateId: evt.TenantAddOnId,
                Provider: PaymentProviderCode.Stripe,
                PayerEmail: SyntheticPayer.EmailFor(evt.TenantId),
                PayerName: null,
                RequestedByUserId: Guid.Empty
            );

            var result = await bus.InvokeAsync<Result<Guid>>(command, ct);
            if (result.IsFailure)
            {
                logger.LogError(
                    "ChargeSaaSPayment failed for renewal of add-on {TenantAddOnId}: {ErrorCode} — {ErrorMessage}",
                    evt.TenantAddOnId,
                    result.Error.Code,
                    result.Error.Message
                );
            }
        }
    }
}
