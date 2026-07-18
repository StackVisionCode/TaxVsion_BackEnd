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

        var secret = secretProtector.Unprotect(config.WebhookSecretEncrypted.CipherText);
        if (string.IsNullOrEmpty(secret))
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
        var alreadyReceived = await webhookEvents.ExistsAsync(
            command.TenantId,
            command.ProviderCode,
            verification.ProviderEventId,
            ct
        );
        if (alreadyReceived)
        {
            metrics.RecordWebhookDuplicate(command.ProviderCode.ToString());
            logger.LogInformation(
                "{Provider} webhook {ProviderEventId} for tenant {TenantId} already processed; skipping (idempotent).",
                command.ProviderCode,
                verification.ProviderEventId,
                command.TenantId
            );
            return Result.Success();
        }

        var nowUtc = DateTime.UtcNow;
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

        var webhookEvent = receiveResult.Value;
        await webhookEvents.AddAsync(webhookEvent, ct);
        webhookEvent.MarkProcessing(nowUtc);

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
        ApplyPayload(payment, payload, metrics);

        webhookEvent.MarkApplied(payment.Id, DateTime.UtcNow);

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

    private static void ApplyPayload(TenantPayment payment, WebhookEventPayload payload, IPaymentClientMetrics metrics)
    {
        var nowUtc = DateTime.UtcNow;

        switch (payload.Status)
        {
            case PaymentStatus.Succeeded:
                payment.MarkSucceeded(nowUtc, Guid.Empty);
                break;

            case PaymentStatus.Failed:
                payment.MarkFailed(
                    payload.FailureCode ?? "Provider.Unknown",
                    payload.FailureMessage ?? "The provider declined the charge.",
                    willRetry: false,
                    nextRetryAtUtc: null,
                    Guid.Empty,
                    nowUtc
                );
                break;

            case PaymentStatus.Cancelled:
                payment.CancelByAdmin("ProviderCancelled", Guid.Empty, nowUtc);
                break;

            case PaymentStatus.Refunded when payload.RefundedAmountCents is { } refundedCents:
                ApplyRefund(payment, refundedCents, nowUtc, metrics);
                break;

            case PaymentStatus.ChargedBack:
                payment.MarkChargedBack(nowUtc, payload.FailureMessage ?? "Chargeback dispute created.", Guid.Empty);
                break;
        }
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
    }

    /// <summary><paramref name="totalRefundedCents"/> es el acumulado en el charge del
    /// provider, no el delta — se resta lo ya registrado localmente para no duplicar.</summary>
    private static void ApplyRefund(
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
            return;

        var deltaMoney = Money.Create(deltaCents, payment.Amount.Currency);
        if (deltaMoney.IsFailure)
            return;

        payment.RefundPartial(deltaMoney.Value, "Refunded via provider webhook.", Guid.Empty, nowUtc);
        metrics.RecordRefund(payment.ProviderCode.ToString());
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
