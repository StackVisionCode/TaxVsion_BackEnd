using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.PaymentLinks;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentClient.Application.TenantPayments.Common;

/// <summary>
/// Aplica a un <see cref="TenantPayment"/> el estado que reporta el proveedor — un webhook verificado o
/// la consulta de la revisión de atascados. Un solo camino: mismas transiciones, misma verificación de
/// monto y mismos eventos de integración, venga de donde venga.
/// </summary>
public static class TenantPaymentProviderUpdate
{
    public static Result Apply(TenantPayment payment, WebhookEventPayload payload, IPaymentClientMetrics metrics)
    {
        var nowUtc = DateTime.UtcNow;

        switch (payload.Status)
        {
            case PaymentStatus.Succeeded:
                var amountMismatch = VerifyPaidAmount(payment, payload);
                if (amountMismatch is not null)
                    return Result.Failure(amountMismatch);
                return payment.MarkSucceeded(nowUtc, Guid.Empty);

            case PaymentStatus.Failed:
                return payment.MarkFailed(
                    payload.FailureCode ?? "Provider.Unknown",
                    payload.FailureMessage ?? "The provider declined the charge.",
                    willRetry: false,
                    nextRetryAtUtc: null,
                    Guid.Empty,
                    nowUtc
                );

            case PaymentStatus.Cancelled:
                return payment.CancelByAdmin("ProviderCancelled", Guid.Empty, nowUtc);

            case PaymentStatus.Refunded when payload.RefundedAmountCents is { } refundedCents:
                return ApplyRefund(payment, refundedCents, nowUtc, metrics);

            case PaymentStatus.ChargedBack:
                return payment.MarkChargedBack(
                    nowUtc,
                    payload.FailureMessage ?? "Chargeback dispute created.",
                    Guid.Empty
                );

            default:
                return Result.Failure(
                    new Error(
                        "WebhookEvent.UnsupportedPaymentStatus",
                        $"Payment status {payload.Status} is not actionable."
                    )
                );
        }
    }

    // F2 defense-in-depth: aunque el evento de éxito esté autenticado por firma, se exige que el monto
    // cobrado que reporta el provider coincida con el cargo esperado (y la moneda). Si no coincide, NO
    // se aplica: el caller lo marca Stale con este código y el pago no queda como "Succeeded" por un
    // importe distinto. Sin monto reportado (null) no se puede verificar → se aplica (compat).
    public const string AmountMismatchCode = "WebhookEvent.AmountMismatch";

    private static Error? VerifyPaidAmount(TenantPayment payment, WebhookEventPayload payload)
    {
        if (payload.PaidAmountCents is not { } paidCents)
            return null;

        var currencyMatches =
            payload.PaidCurrency is null
            || string.Equals(payload.PaidCurrency, payment.Amount.Currency, StringComparison.OrdinalIgnoreCase);

        if (paidCents == payment.Amount.AmountCents && currencyMatches)
            return null;

        return new Error(
            AmountMismatchCode,
            $"Provider reported {paidCents} {payload.PaidCurrency ?? "?"} paid but the charge expects "
                + $"{payment.Amount.AmountCents} {payment.Amount.Currency}; not applying."
        );
    }

    /// <summary>
    /// Completa el <c>PaymentLink</c> que originó este cobro, si lo hay — cubre el caso 3DS/SCA donde
    /// <c>RedeemPaymentLinkHandler</c> no pudo confirmar el éxito sincrónicamente (§F.4). Devuelve el
    /// evento del link SIN publicarlo (el caller guarda y luego llama <see cref="PublishSucceededAsync"/>).
    /// Null = no había link que completar o estaba revocado. La señal "pagado" no depende de esto.
    /// </summary>
    public static async Task<PaymentLinkUsedIntegrationEvent?> CompletePaymentLinkIfAnyAsync(
        TenantPayment payment,
        IPaymentLinkRepository links,
        IPaymentAuditLogWriter audit,
        IPaymentClientMetrics metrics,
        ICorrelationContext correlation,
        string source,
        CancellationToken ct
    )
    {
        var link = await links.GetByRelatedTenantPaymentIdAsync(payment.Id, ct);
        if (link is null)
            return null;

        var nowUtc = DateTime.UtcNow;
        var completed = link.CompleteWithAttempt(payment.Id, nowUtc);
        if (completed.IsFailure)
        {
            // Link revocado (factura anulada) antes de confirmar el pago: solo queda auditado.
            if (completed.Error.Code == PaymentLink.ClosedBeforePaymentError.Code)
                await AuditEntryFactory.AppendAsync(
                    audit,
                    link.TenantId,
                    nameof(PaymentLink),
                    link.Id,
                    PaymentAuditAction.PaymentLinkClosedBeforePayment,
                    actorUserId: Guid.Empty,
                    correlation.CorrelationId,
                    before: (object?)null,
                    after: new { TenantPaymentId = payment.Id, Source = source },
                    reason: completed.Error.Code,
                    nowUtc,
                    ct
                );
            return null;
        }

        metrics.RecordPaymentLinkUsed();

        await AuditEntryFactory.AppendAsync(
            audit,
            link.TenantId,
            nameof(PaymentLink),
            link.Id,
            PaymentAuditAction.PaymentLinkUsed,
            actorUserId: Guid.Empty,
            correlation.CorrelationId,
            before: (object?)null,
            after: new
            {
                payment.Id,
                link.UsedAtUtc,
                Source = source,
            },
            reason: null,
            nowUtc,
            ct
        );

        return new PaymentLinkUsedIntegrationEvent
        {
            TenantId = link.TenantId,
            PaymentLinkId = link.Id,
            TenantPaymentId = payment.Id,
            AmountCents = link.Amount.AmountCents,
            Currency = link.Amount.Currency,
            UsedAtUtc = nowUtc,
            CorrelationId = correlation.CorrelationId,
        };
    }

    /// <summary>Éxito del proveedor que no se pudo aplicar y que el dueño revisa a mano: pago ya cerrado
    /// sin éxito (Failed/Cancelled) o monto distinto. Un éxito repetido sobre un pago pagado sigue Stale.</summary>
    public static bool IsUnappliedProviderSuccess(TenantPayment payment, WebhookEventPayload payload, Error error) =>
        payload.Status == PaymentStatus.Succeeded
        && (
            error.Code == AmountMismatchCode
            || (
                error.Code == "TenantPayment.InvalidTransition"
                && payment.Status is PaymentStatus.Failed or PaymentStatus.Cancelled
            )
        );

    /// <summary>Métrica (alerta) y auditoría del éxito sin aplicar. Sin reembolso ni cambio de estado.</summary>
    public static async Task RecordNeedsReviewAsync(
        TenantPayment payment,
        string reason,
        string source,
        IPaymentAuditLogWriter audit,
        IPaymentClientMetrics metrics,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        metrics.RecordWebhookNeedsReview(payment.ProviderCode.ToString(), reason);
        await AuditEntryFactory.AppendAsync(
            audit,
            payment.TenantId,
            nameof(TenantPayment),
            payment.Id,
            PaymentAuditAction.TenantPaymentProviderSuccessNeedsReview,
            actorUserId: Guid.Empty,
            correlation.CorrelationId,
            before: (object?)null,
            after: new { payment.Status, Source = source },
            reason: reason,
            DateTime.UtcNow,
            ct
        );
    }

    /// <summary>Publica el link usado (si hubo) y la señal "pagado"; llamar solo después de guardar.</summary>
    public static async Task PublishSucceededAsync(
        IMessageBus bus,
        TenantPayment payment,
        PaymentLinkUsedIntegrationEvent? linkUsed,
        string? correlationId
    )
    {
        if (linkUsed is not null)
            await bus.PublishAsync(linkUsed);

        await bus.PublishAsync(
            TenantPaymentSucceededSignal.Build(payment, payment.PaidAtUtc ?? DateTime.UtcNow, correlationId)
        );
    }

    /// <summary><paramref name="totalRefundedCents"/> es el acumulado en el charge del
    /// provider, no el delta — se resta lo ya registrado localmente para no duplicar.</summary>
    private static Result ApplyRefund(
        TenantPayment payment,
        long totalRefundedCents,
        DateTime nowUtc,
        IPaymentClientMetrics metrics
    )
    {
        long alreadyTracked = 0;
        foreach (var line in payment.Refunds)
            alreadyTracked += line.Amount.AmountCents;

        var deltaCents = totalRefundedCents - alreadyTracked;
        if (deltaCents <= 0)
            return Result.Success();

        var deltaMoney = Money.Create(deltaCents, payment.Amount.Currency);
        if (deltaMoney.IsFailure)
            return Result.Failure(deltaMoney.Error);

        var refundResult = payment.RefundPartial(
            deltaMoney.Value,
            "Refunded via provider webhook.",
            Guid.Empty,
            nowUtc
        );
        if (refundResult.IsFailure)
            return refundResult;

        metrics.RecordRefund(payment.ProviderCode.ToString());
        return Result.Success();
    }

    public static PaymentAuditAction MapAuditAction(PaymentStatus status) =>
        status switch
        {
            PaymentStatus.Succeeded => PaymentAuditAction.TenantPaymentSucceeded,
            PaymentStatus.Failed => PaymentAuditAction.TenantPaymentFailed,
            PaymentStatus.Cancelled => PaymentAuditAction.TenantPaymentCancelled,
            PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded =>
                PaymentAuditAction.TenantPaymentRefundedPartial,
            PaymentStatus.ChargedBack => PaymentAuditAction.TenantPaymentChargedBack,
            _ => PaymentAuditAction.TenantPaymentMarkedProcessing,
        };
}
