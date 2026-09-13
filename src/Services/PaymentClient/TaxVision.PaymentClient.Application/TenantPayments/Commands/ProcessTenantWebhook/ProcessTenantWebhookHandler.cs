using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.PaymentLinks;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Domain.Webhooks;
using Wolverine;

namespace TaxVision.PaymentClient.Application.TenantPayments.Commands.ProcessTenantWebhook;

/// <summary>
/// Signature verification obligatoria — este handler es el único lugar donde un payload de
/// provider se trata como confiable, y solo después de
/// <see cref="IPaymentProvider.VerifyWebhookSignatureAsync"/> contra el
/// <c>WebhookSecretEncrypted</c> DE ESE TENANT (a diferencia de PaymentApp, que tiene un solo
/// secret global). Dedupe por <c>(TenantId, ProviderCode, ProviderEventId)</c> antes de mutar
/// nada — un reintento del mismo evento es un no-op idempotente.
/// </summary>
public static class ProcessTenantWebhookHandler
{
    public static async Task<Result> Handle(
        ProcessTenantWebhookCommand command,
        ITenantPaymentConfigRepository configs,
        IPaymentAdapterFactory providerFactory,
        ISecretProtector secretProtector,
        IWebhookEventRepository webhookEvents,
        ITenantPaymentRepository payments,
        IPaymentLinkRepository links,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        IPaymentClientMetrics metrics,
        ICorrelationContext correlation,
        ILogger<WebhookEvent> logger,
        CancellationToken ct
    )
    {
        metrics.RecordWebhookReceived(command.ProviderCode.ToString());

        var config = await configs.GetByTenantAndProviderAsync(command.TenantId, command.ProviderCode, ct);
        if (config?.WebhookSecretEncrypted is null)
            return Result.Failure(
                new Error(
                    "TenantPaymentConfig.WebhookSecretMissing",
                    "Webhook secret is not configured for this tenant."
                )
            );

        if (!secretProtector.TryUnprotect(config.WebhookSecretEncrypted.CipherText, out var secret, out _))
            return Result.Failure(
                new Error("TenantPaymentConfig.WebhookSecretMissing", "Webhook secret could not be decrypted.")
            );

        var adapter = providerFactory.Resolve(command.ProviderCode);
        var verificationResult = await adapter.VerifyWebhookSignatureAsync(
            command.RawPayload,
            command.SignatureHeader,
            secret,
            ct
        );
        if (verificationResult.IsFailure)
        {
            metrics.RecordWebhookSignatureFailed(command.ProviderCode.ToString());
            logger.LogWarning(
                "Rejected {Provider} webhook for tenant {TenantId} with invalid signature: {Error}",
                command.ProviderCode,
                command.TenantId,
                verificationResult.Error.Message
            );
            return Result.Failure(verificationResult.Error);
        }

        var verification = verificationResult.Value;
        var nowUtc = DateTime.UtcNow;

        // Idempotencia STATUS-AWARE (F1): un evento en estado terminal ya fue resuelto → se descarta
        // como duplicado; uno NO terminal (Failed) quedó a medias por un fallo transitorio y esta nueva
        // entrega del provider lo RE-PROCESA — así un cobro real nunca se pierde por un reintento
        // tratado como duplicado.
        var existing = await webhookEvents.GetByProviderEventIdAsync(
            command.TenantId,
            command.ProviderCode,
            verification.ProviderEventId,
            ct
        );
        WebhookEvent webhookEvent;
        if (existing is not null)
        {
            if (existing.IsTerminal)
            {
                metrics.RecordWebhookDuplicate(command.ProviderCode.ToString());
                logger.LogInformation(
                    "{Provider} webhook {ProviderEventId} for tenant {TenantId} already {Status}; skipping (idempotent).",
                    command.ProviderCode,
                    verification.ProviderEventId,
                    command.TenantId,
                    existing.Status
                );
                return Result.Success();
            }

            var reprocessResult = existing.MarkReprocessing(nowUtc);
            if (reprocessResult.IsFailure)
                return Result.Failure(reprocessResult.Error);
            webhookEvent = existing;
            logger.LogWarning(
                "{Provider} webhook {ProviderEventId} for tenant {TenantId} was not applied on a previous delivery ({Status}); reprocessing.",
                command.ProviderCode,
                verification.ProviderEventId,
                command.TenantId,
                existing.Status
            );
        }
        else
        {
            var receiveResult = WebhookEvent.Receive(
                command.TenantId,
                command.ProviderCode,
                verification.ProviderEventId,
                verification.EventType,
                command.RawPayload,
                command.SignatureHeader,
                nowUtc
            );
            if (receiveResult.IsFailure)
                return Result.Failure(receiveResult.Error);

            webhookEvent = receiveResult.Value;
            await webhookEvents.AddAsync(webhookEvent, ct);
            webhookEvent.MarkProcessing(nowUtc);
        }

        var payloadResult = await adapter.ParseWebhookEventAsync(command.RawPayload, verification.EventType, ct);
        if (payloadResult.IsFailure)
        {
            if (payloadResult.Error.Code == "Stripe.Webhook.UnsupportedEventType")
            {
                webhookEvent.MarkRejected(payloadResult.Error.Message, DateTime.UtcNow);
                await unitOfWork.SaveChangesAsync(ct);
                return Result.Success();
            }

            webhookEvent.MarkFailed(payloadResult.Error.Message, DateTime.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Failure(payloadResult.Error);
        }

        var payload = payloadResult.Value;
        var payment = await payments.GetByExternalReferenceAsync(
            command.TenantId,
            command.ProviderCode,
            payload.ProviderChargeReference,
            ct
        );
        if (payment is null)
        {
            logger.LogWarning(
                "{Provider} webhook {ProviderEventId} for tenant {TenantId} references unknown charge {Reference}; rejecting.",
                command.ProviderCode,
                verification.ProviderEventId,
                command.TenantId,
                payload.ProviderChargeReference
            );
            webhookEvent.MarkRejected("No matching TenantPayment for this charge reference.", DateTime.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success();
        }

        var statusBeforeWebhook = payment.Status;
        var transitionResult = ApplyPayload(payment, payload, metrics);
        if (transitionResult.IsFailure)
        {
            webhookEvent.MarkStale(payment.Id, transitionResult.Error.Code, DateTime.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "{Provider} webhook {EventType} ({ProviderEventId}) is stale for TenantPayment {TenantPaymentId}: {ErrorCode}.",
                command.ProviderCode,
                verification.EventType,
                verification.ProviderEventId,
                payment.Id,
                transitionResult.Error.Code
            );
            return Result.Success();
        }

        var appliedResult = webhookEvent.MarkApplied(payment.Id, DateTime.UtcNow);
        if (appliedResult.IsFailure)
            return appliedResult;

        await AuditEntryFactory.AppendAsync(
            audit,
            payment.TenantId,
            nameof(TenantPayment),
            payment.Id,
            MapAuditAction(payment.Status),
            actorUserId: Guid.Empty,
            correlation.CorrelationId,
            before: (object?)null,
            after: new
            {
                payment.Status,
                Source = $"{command.ProviderCode}Webhook",
                verification.EventType,
            },
            reason: null,
            DateTime.UtcNow,
            ct
        );

        // Stripe reenvía payment_intent.succeeded incluso para pagos que ya confirmamos
        // sincrónicamente — solo contamos GMV si ESTE webhook fue el que causó la transición
        // (el caso 3DS/SCA async), nunca si el pago ya estaba Succeeded de antes.
        if (payment.Status == PaymentStatus.Succeeded && statusBeforeWebhook != PaymentStatus.Succeeded)
        {
            metrics.RecordPaymentSucceeded(payment.Amount.AmountCents, payment.Amount.Currency);
            await CompletePaymentLinkIfAnyAsync(payment, links, audit, bus, metrics, correlation, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "{Provider} webhook {EventType} ({ProviderEventId}) applied to TenantPayment {TenantPaymentId}: now {Status}.",
            command.ProviderCode,
            verification.EventType,
            verification.ProviderEventId,
            payment.Id,
            payment.Status
        );

        return Result.Success();
    }

    private static Result ApplyPayload(
        TenantPayment payment,
        WebhookEventPayload payload,
        IPaymentClientMetrics metrics
    )
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
            "WebhookEvent.AmountMismatch",
            $"Provider reported {paidCents} {payload.PaidCurrency ?? "?"} paid but the charge expects "
                + $"{payment.Amount.AmountCents} {payment.Amount.Currency}; not applying."
        );
    }

    /// <summary>Completa el <c>PaymentLink</c> que originó este cobro, si lo hay — cubre el
    /// caso 3DS/SCA donde <c>RedeemPaymentLinkHandler</c> no pudo confirmar el éxito
    /// sincrónicamente y quedó esperando esta misma confirmación por webhook (§F.4).</summary>
    private static async Task CompletePaymentLinkIfAnyAsync(
        TenantPayment payment,
        IPaymentLinkRepository links,
        IPaymentAuditLogWriter audit,
        IMessageBus bus,
        IPaymentClientMetrics metrics,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var link = await links.GetByRelatedTenantPaymentIdAsync(payment.Id, ct);
        if (link is null)
            return;

        var nowUtc = DateTime.UtcNow;
        var markUsedResult = link.MarkAsUsed(nowUtc);
        if (markUsedResult.IsFailure)
            return;

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
                Source = "Webhook",
            },
            reason: null,
            nowUtc,
            ct
        );

        await bus.PublishAsync(
            new PaymentLinkUsedIntegrationEvent
            {
                TenantId = link.TenantId,
                PaymentLinkId = link.Id,
                TenantPaymentId = payment.Id,
                AmountCents = link.Amount.AmountCents,
                Currency = link.Amount.Currency,
                UsedAtUtc = nowUtc,
                CorrelationId = correlation.CorrelationId,
            }
        );

        // Fase 3: señal "pagado" con la referencia externa (id de factura), también en el camino async
        // del webhook (3DS/SCA) — Billing la consume para marcar la factura Paid.
        await bus.PublishAsync(
            new TenantPaymentSucceededIntegrationEvent
            {
                TenantId = payment.TenantId,
                TenantPaymentId = payment.Id,
                ProviderCode = payment.ProviderCode.ToString(),
                PurposeKind = payment.Purpose.Kind.ToString(),
                ExternalReferenceId = payment.Purpose.ExternalReferenceId,
                AmountCents = payment.Amount.AmountCents,
                Currency = payment.Amount.Currency,
                PaidAtUtc = nowUtc,
                CorrelationId = correlation.CorrelationId,
            }
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

    private static PaymentAuditAction MapAuditAction(PaymentStatus status) =>
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
