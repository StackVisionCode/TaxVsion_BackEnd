using BuildingBlocks.Messaging.PaymentIntegrationEvents;
using BuildingBlocks.Tenancy;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.SaaSPayments.Events;
using Wolverine;

namespace TaxVision.PaymentApp.Application.SaaSPayments.DomainEventHandlers;

/// <summary>
/// Resultado de un SaaSPayment: devuelve los avisos de integración (cascada por el outbox). Corre después del
/// commit, desde la cola domain-events. Lo que llevan los avisos sale de la foto del evento; solo se leen el
/// perfil de la oficina (recibo) y la tarjeta guardada (correo de cobro fallido), con el tenant explícito.
/// </summary>
public static class SaaSPaymentDomainEventHandler
{
    public static Task<OutgoingMessages> Handle(
        SaaSPaymentSucceeded succeeded,
        ITenantRegistry tenants,
        ITenantProviderCustomerRepository customers,
        Envelope envelope,
        CancellationToken ct
    ) => ResultAsync(succeeded.Payment, tenants, customers, envelope, ct);

    public static Task<OutgoingMessages> Handle(
        SaaSPaymentFailed failed,
        ITenantRegistry tenants,
        ITenantProviderCustomerRepository customers,
        Envelope envelope,
        CancellationToken ct
    ) => ResultAsync(failed.Payment, tenants, customers, envelope, ct);

    // La cancelación que pidió el dueño del cobro no se le anuncia: ya la conoce.
    public static Task<OutgoingMessages> Handle(
        SaaSPaymentCancelled cancelled,
        ITenantRegistry tenants,
        ITenantProviderCustomerRepository customers,
        Envelope envelope,
        CancellationToken ct
    ) =>
        cancelled.PropagatedFromOwner
            ? Task.FromResult(new OutgoingMessages())
            : ResultAsync(cancelled.Payment, tenants, customers, envelope, ct);

    /// <summary>
    /// Reembolso del pago: se anuncia con el sobre financiero genérico para quien tenga una consecuencia comercial
    /// (Growth revierte el premio de referido). El PaymentId es el del pago de PaymentApp, el mismo con el que
    /// otros servicios lo calificaron. Un onboarding aún no tiene tenant: viaja con el de plataforma.
    /// </summary>
    public static OutgoingMessages Handle(SaaSPaymentRefunded refunded, Envelope envelope)
    {
        var payment = refunded.Payment;
        return new OutgoingMessages
        {
            new PaymentRefundedIntegrationEvent
            {
                TenantId = EventTenant(payment),
                AggregateId = payment.TargetAggregateId,
                AggregateVersion = 1,
                PaymentSource = "PaymentApp",
                PaymentId = payment.SaaSPaymentId,
                GrossAmountCents = payment.AmountCents + DiscountOf(payment),
                DiscountAmountCents = DiscountOf(payment),
                NetAmountCents = payment.AmountCents,
                Currency = payment.Currency,
                Promotions = PromotionsOf(payment),
                RefundReference = $"{payment.SaaSPaymentId:N}:{refunded.CumulativeRefundedCents}",
                RefundDeltaAmountCents = refunded.RefundDeltaCents,
                CumulativeRefundedAmountCents = refunded.CumulativeRefundedCents,
                RefundedAtUtc = refunded.RefundedAtUtc,
                CorrelationId = envelope.CorrelationId ?? string.Empty,
            },
        };
    }

    /// <summary>
    /// Chargeback: el proveedor retira los fondos al abrir la disputa ("opened", versión 1). Quien reacciona (Growth)
    /// retiene y decide al cierre, que llega como "won" o "lost" (versión 2).
    /// </summary>
    public static OutgoingMessages Handle(SaaSPaymentChargedBack chargedBack, Envelope envelope) =>
        [ChargebackChanged(chargedBack.Payment, "opened", 1, chargedBack.ChargedBackAtUtc, envelope)];

    public static OutgoingMessages Handle(SaaSPaymentChargebackResolved resolved, Envelope envelope) =>
        [
            ChargebackChanged(
                resolved.Payment,
                resolved.Outcome == ChargebackOutcome.Won ? "won" : "lost",
                2,
                resolved.ResolvedAtUtc,
                envelope
            ),
        ];

    private static PaymentChargebackChangedIntegrationEvent ChargebackChanged(
        SaaSPaymentSnapshot payment,
        string disputeStatus,
        long disputeVersion,
        DateTime changedAtUtc,
        Envelope envelope
    ) =>
        new()
        {
            TenantId = EventTenant(payment),
            AggregateId = payment.TargetAggregateId,
            AggregateVersion = 1,
            PaymentSource = "PaymentApp",
            PaymentId = payment.SaaSPaymentId,
            GrossAmountCents = payment.AmountCents + DiscountOf(payment),
            DiscountAmountCents = DiscountOf(payment),
            NetAmountCents = payment.AmountCents,
            Currency = payment.Currency,
            Promotions = PromotionsOf(payment),
            DisputeId = $"chargeback:{payment.SaaSPaymentId:N}",
            DisputeStatus = disputeStatus,
            DisputeVersion = disputeVersion,
            DisputedAmountCents = payment.AmountCents,
            ChangedAtUtc = changedAtUtc,
            CorrelationId = envelope.CorrelationId ?? string.Empty,
        };

    // El alta guarda sus descuentos como promociones; el resto de cargos, en DiscountAmountCents.
    private static long DiscountOf(SaaSPaymentSnapshot payment) =>
        payment.DiscountAmountCents ?? payment.Promotions?.Sum(promotion => promotion.DiscountAmountCents) ?? 0;

    private static IReadOnlyList<PaymentPromotionData>? PromotionsOf(SaaSPaymentSnapshot payment) =>
        payment.Promotions is null
            ? null
            :
            [
                .. payment.Promotions.Select(promotion => new PaymentPromotionData(
                    promotion.CodeReservationId,
                    promotion.DiscountAmountCents,
                    promotion.Order
                )),
            ];

    private static Guid EventTenant(SaaSPaymentSnapshot payment) =>
        payment.OnboardingId is not null || payment.TenantId == Guid.Empty ? PlatformTenant.Id : payment.TenantId;

    // Sin consumidor a propósito: la revisión se ve en el panel del admin (métrica y log en el comando).
    public static void Handle(SaaSPaymentReviewRequired reviewRequired) { }

    private static async Task<OutgoingMessages> ResultAsync(
        SaaSPaymentSnapshot payment,
        ITenantRegistry tenants,
        ITenantProviderCustomerRepository customers,
        Envelope envelope,
        CancellationToken ct
    )
    {
        var messages = new OutgoingMessages();
        messages.AddRange(
            await SaaSPaymentResultMessages.BuildAsync(
                payment,
                envelope.CorrelationId ?? string.Empty,
                ct,
                tenants,
                customers
            )
        );
        return messages;
    }
}
