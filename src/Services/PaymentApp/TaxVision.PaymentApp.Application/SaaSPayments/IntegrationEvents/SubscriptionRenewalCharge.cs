using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Platform;
using BuildingBlocks.Results;
using TaxVision.PaymentApp.Application.Common;
using TaxVision.PaymentApp.Application.Common.Branding;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ChargeSaaSPayment;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.SaaSPayments.IntegrationEvents;

/// <summary>Arma el cobro de una renovación con los datos del evento, en cualquiera de las dos estrategias.</summary>
internal static class SubscriptionRenewalCharge
{
    public static Result<SaaSPayment> BuildPayment(
        SubscriptionRenewalDueIntegrationEvent evt,
        PaymentProviderCode provider,
        IPlatformSettings platform
    )
    {
        var keyResult = IdempotencyKey.Create(evt.IdempotencyKey);
        if (keyResult.IsFailure)
            return Result.Failure<SaaSPayment>(keyResult.Error);

        var amountResult = Money.Create(evt.AmountCents, evt.Currency);
        if (amountResult.IsFailure)
            return Result.Failure<SaaSPayment>(amountResult.Error);

        var descriptorResult = StatementDescriptor.Create(PlatformStatementDescriptors.For(platform.Current, "SAAS"));
        if (descriptorResult.IsFailure)
            return Result.Failure<SaaSPayment>(descriptorResult.Error);

        return SaaSPayment
            .Create(
                evt.TenantId,
                keyResult.Value,
                amountResult.Value,
                SaaSPaymentType.SubscriptionRenewal,
                evt.TenantSubscriptionId,
                provider,
                descriptorResult.Value,
                actorUserId: Guid.Empty,
                DateTime.UtcNow,
                evt.CodeReservationId,
                evt.CodeReservationPaymentId,
                evt.DiscountAmountCents,
                evt.PromotionSnapshotHash
            )
            .WithConceptFor(SaaSPaymentType.SubscriptionRenewal);
    }

    // Respeta el provider del tenant también en el fallback: con Stripe fijo, la renovación entraba en loop de
    // "Failed — provider declined" para cualquier tenant con PayPal.
    public static ChargeSaaSPaymentCommand BuildLegacyCommand(
        SubscriptionRenewalDueIntegrationEvent evt,
        PaymentProviderCode provider
    ) =>
        new(
            TenantId: evt.TenantId,
            IdempotencyKey: evt.IdempotencyKey,
            AmountCents: evt.AmountCents,
            Currency: evt.Currency,
            Type: SaaSPaymentType.SubscriptionRenewal,
            TargetAggregateId: evt.TenantSubscriptionId,
            Provider: provider,
            PayerEmail: SyntheticPayer.EmailFor(evt.TenantId),
            PayerName: null,
            RequestedByUserId: Guid.Empty,
            CodeReservationId: evt.CodeReservationId,
            CodeReservationPaymentId: evt.CodeReservationPaymentId,
            DiscountAmountCents: evt.DiscountAmountCents,
            PromotionSnapshotHash: evt.PromotionSnapshotHash
        );
}
