using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Application.TenantPayments.Common;
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

        // PayPal verifica contra su API con las claves de la oficina; Stripe solo usa el webhook secret.
        var secretKey =
            config.SecretKeyEncrypted is not null
            && secretProtector.TryUnprotect(config.SecretKeyEncrypted.CipherText, out var plainKey, out _)
                ? plainKey
                : string.Empty;
        var credentials = new TenantProviderCredentials(secretKey, secret, config.PublishableKey, config.ApiBaseUrl);

        var adapter = providerFactory.Resolve(command.ProviderCode);
        var verificationResult = await adapter.VerifyWebhookSignatureAsync(
            command.RawPayload,
            command.Headers,
            credentials,
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
                verification.Signature ?? string.Empty,
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
            if (payloadResult.Error.Code == WebhookEventPayload.UnsupportedEventTypeCode)
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
        var transitionResult = TenantPaymentProviderUpdate.Apply(payment, payload, metrics);
        if (transitionResult.IsFailure)
        {
            // Éxito del proveedor que no se aplicó: revisión visible, no "Stale" en silencio (PCL-4).
            if (TenantPaymentProviderUpdate.IsUnappliedProviderSuccess(payment, payload, transitionResult.Error))
            {
                webhookEvent.MarkNeedsReview(payment.Id, transitionResult.Error.Code, DateTime.UtcNow);
                await TenantPaymentProviderUpdate.RecordNeedsReviewAsync(
                    payment,
                    transitionResult.Error.Code,
                    $"{command.ProviderCode}Webhook",
                    audit,
                    metrics,
                    correlation,
                    ct
                );
                await unitOfWork.SaveChangesAsync(ct);
                logger.LogWarning(
                    "{Provider} webhook {EventType} ({ProviderEventId}) reported a success that was not applied to TenantPayment {TenantPaymentId} ({Status}): {ErrorCode}. Needs review.",
                    command.ProviderCode,
                    verification.EventType,
                    verification.ProviderEventId,
                    payment.Id,
                    payment.Status,
                    transitionResult.Error.Code
                );
                return Result.Success();
            }

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
            TenantPaymentProviderUpdate.MapAuditAction(payment.Status),
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
        var becameSucceeded =
            payment.Status == PaymentStatus.Succeeded && statusBeforeWebhook != PaymentStatus.Succeeded;
        PaymentLinkUsedIntegrationEvent? linkUsed = null;
        if (becameSucceeded)
        {
            metrics.RecordPaymentSucceeded(payment.Amount.AmountCents, payment.Amount.Currency);
            linkUsed = await TenantPaymentProviderUpdate.CompletePaymentLinkIfAnyAsync(
                payment,
                links,
                audit,
                metrics,
                correlation,
                source: "Webhook",
                ct
            );
        }

        // Guardar antes de publicar: un reintento en línea no debe filtrar eventos del intento revertido.
        await unitOfWork.SaveChangesAsync(ct);
        if (becameSucceeded)
            await TenantPaymentProviderUpdate.PublishSucceededAsync(bus, payment, linkUsed, correlation.CorrelationId);

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
}
