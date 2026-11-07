using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.Webhooks;
using Wolverine;

namespace TaxVision.PaymentClient.Application.TenantPayments.Common;

/// <summary>
/// Aplica un evento verificado a un pago ya resuelto y cierra el <see cref="WebhookEvent"/>. Lo comparten el webhook
/// de la oficina y el de Connect (PCL-9): mismas transiciones, misma revisión y mismos avisos.
/// </summary>
public static class TenantPaymentWebhookApplier
{
    public static async Task<Result> ApplyAsync(
        TenantPayment payment,
        WebhookEvent webhookEvent,
        WebhookEventPayload payload,
        string source,
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
                    source,
                    audit,
                    metrics,
                    correlation,
                    ct
                );
                await unitOfWork.SaveChangesAsync(ct);
                logger.LogWarning(
                    "{Source} {EventType} ({ProviderEventId}) reported a success that was not applied to TenantPayment {TenantPaymentId} ({Status}): {ErrorCode}. Needs review.",
                    source,
                    webhookEvent.EventType,
                    webhookEvent.ProviderEventId,
                    payment.Id,
                    payment.Status,
                    transitionResult.Error.Code
                );
                return Result.Success();
            }

            webhookEvent.MarkStale(payment.Id, transitionResult.Error.Code, DateTime.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "{Source} {EventType} ({ProviderEventId}) is stale for TenantPayment {TenantPaymentId}: {ErrorCode}.",
                source,
                webhookEvent.EventType,
                webhookEvent.ProviderEventId,
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
                Source = source,
                webhookEvent.EventType,
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
            await TenantPaymentProviderUpdate.PublishLinkUsedAsync(bus, linkUsed);

        logger.LogInformation(
            "{Source} {EventType} ({ProviderEventId}) applied to TenantPayment {TenantPaymentId}: now {Status}.",
            source,
            webhookEvent.EventType,
            webhookEvent.ProviderEventId,
            payment.Id,
            payment.Status
        );

        return Result.Success();
    }
}
