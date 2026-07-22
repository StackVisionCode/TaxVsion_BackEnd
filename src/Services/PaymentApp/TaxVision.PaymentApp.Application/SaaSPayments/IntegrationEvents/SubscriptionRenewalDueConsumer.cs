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
/// Consume el intent de renovación publicado por Subscription y lo traduce a un
/// <see cref="ChargeSaaSPaymentCommand"/>. Fase A no tiene todavía <c>TenantProviderCustomer</c>
/// (aggregate de Fase D) ni el email real del admin del tenant — se usa un email sintético
/// determinístico para registrar el customer en el provider. Cuando Fase D exista, este
/// consumer se actualiza para resolver el email real desde el registro de customer guardado.
/// </summary>
public static class SubscriptionRenewalDueConsumer
{
    public static async Task Handle(
        SubscriptionRenewalDueIntegrationEvent evt,
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
                Type: SaaSPaymentType.SubscriptionRenewal,
                TargetAggregateId: evt.TenantSubscriptionId,
                Provider: PaymentProviderCode.Stripe,
                PayerEmail: SyntheticPayer.EmailFor(evt.TenantId),
                PayerName: null,
                RequestedByUserId: Guid.Empty,
                CodeReservationId: evt.CodeReservationId,
                CodeReservationPaymentId: evt.CodeReservationPaymentId,
                DiscountAmountCents: evt.DiscountAmountCents,
                PromotionSnapshotHash: evt.PromotionSnapshotHash
            );

            var result = await bus.InvokeAsync<Result<Guid>>(command, ct);
            if (result.IsFailure)
            {
                logger.LogError(
                    "ChargeSaaSPayment failed for renewal of subscription {TenantSubscriptionId}: {ErrorCode} — {ErrorMessage}",
                    evt.TenantSubscriptionId,
                    result.Error.Code,
                    result.Error.Message
                );
            }
        }
    }
}
