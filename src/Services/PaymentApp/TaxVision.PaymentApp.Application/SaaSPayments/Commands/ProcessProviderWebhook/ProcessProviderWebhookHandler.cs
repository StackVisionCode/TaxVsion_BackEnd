using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.Common;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ProcessStripeWebhook;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Domain.Webhooks;
using Wolverine;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.ProcessProviderWebhook;

public static class ProcessProviderWebhookHandler
{
    public static Task<Result> Handle(
        ProcessProviderWebhookCommand command,
        IPaymentAdapterFactory providerFactory,
        IProviderWebhookSecrets webhookSecrets,
        IWebhookEventRepository webhookEvents,
        ISaaSPaymentRepository payments,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IPaymentAppMetrics metrics,
        IPaymentAttemptThrottle throttle,
        ICorrelationContext correlation,
        IMessageBus bus,
        ILogger<WebhookEvent> logger,
        CancellationToken ct
    ) =>
        ProcessAsync(
            command.Provider,
            command.RawPayload,
            command.Headers,
            providerFactory,
            webhookSecrets,
            webhookEvents,
            payments,
            audit,
            unitOfWork,
            metrics,
            throttle,
            correlation,
            bus,
            logger,
            ct
        );

    public static async Task<Result> ProcessAsync(
        PaymentProviderCode provider,
        string rawPayload,
        IReadOnlyDictionary<string, string> headers,
        IPaymentAdapterFactory providerFactory,
        IProviderWebhookSecrets webhookSecrets,
        IWebhookEventRepository webhookEvents,
        ISaaSPaymentRepository payments,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IPaymentAppMetrics metrics,
        IPaymentAttemptThrottle throttle,
        ICorrelationContext correlation,
        IMessageBus bus,
        ILogger<WebhookEvent> logger,
        CancellationToken ct
    )
    {
        metrics.RecordWebhookReceived(provider.ToString());

        IPaymentProvider adapter;
        try
        {
            adapter = providerFactory.Resolve(provider);
        }
        catch (InvalidOperationException)
        {
            return Result.Failure(
                new Error("PaymentProvider.NotConfigured", "The selected payment provider is not configured.")
            );
        }

        var verificationResult = await adapter.VerifyWebhookSignatureAsync(
            new ProviderWebhookVerificationRequest(
                rawPayload,
                headers,
                webhookSecrets.GetWebhookSecret(provider),
                webhookSecrets.GetWebhookId(provider)
            ),
            ct
        );
        if (verificationResult.IsFailure)
        {
            metrics.RecordWebhookSignatureFailed(provider.ToString());
            logger.LogWarning(
                "Rejected {Provider} webhook with invalid signature: {Error}",
                provider,
                verificationResult.Error.Message
            );
            return Result.Failure(verificationResult.Error);
        }

        var verification = verificationResult.Value;
        var alreadyReceived = await webhookEvents.ExistsAsync(provider, verification.ProviderEventId, ct);
        if (alreadyReceived)
        {
            metrics.RecordWebhookDuplicate(provider.ToString());
            logger.LogInformation(
                "{Provider} webhook {ProviderEventId} already processed; skipping (idempotent).",
                provider,
                verification.ProviderEventId
            );
            return Result.Success();
        }

        var nowUtc = DateTime.UtcNow;
        var receiveResult = WebhookEvent.Receive(
            provider,
            verification.ProviderEventId,
            verification.EventType,
            rawPayload,
            BuildSignatureSnapshot(provider, headers),
            nowUtc
        );
        if (receiveResult.IsFailure)
            return Result.Failure(receiveResult.Error);

        var webhookEvent = receiveResult.Value;
        await webhookEvents.AddAsync(webhookEvent, ct);
        webhookEvent.MarkProcessing(nowUtc);
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (ConflictException ex) when (ex.Code == "Persistence.UniqueConstraint")
        {
            metrics.RecordWebhookDuplicate(provider.ToString());
            logger.LogInformation(
                "{Provider} webhook {ProviderEventId} was inserted by a concurrent delivery; skipping (idempotent).",
                provider,
                verification.ProviderEventId
            );
            return Result.Success();
        }

        var payloadResult = await adapter.ParseWebhookEventAsync(rawPayload, verification.EventType, ct);
        if (payloadResult.IsFailure)
        {
            if (IsUnsupportedWebhookEvent(payloadResult.Error))
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
        var payment = await payments.GetByExternalReferenceAsync(provider, payload.ProviderChargeReference, ct);
        if (payment is null)
        {
            logger.LogWarning(
                "{Provider} webhook {ProviderEventId} references unknown charge {Reference}; rejecting.",
                provider,
                verification.ProviderEventId,
                payload.ProviderChargeReference
            );
            webhookEvent.MarkRejected("No matching SaaSPayment for this charge reference.", DateTime.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success();
        }

        if (await throttle.IsWebhookThrottledAsync(payment.TenantId, ct))
        {
            logger.LogWarning(
                "Webhook throttled for tenant {TenantId}: too many webhook events in the last minute.",
                payment.TenantId
            );
            webhookEvent.MarkRejected("Tenant webhook rate exceeded.", DateTime.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success();
        }

        await throttle.RegisterWebhookAttemptAsync(payment.TenantId, ct);
        ReconcileProviderReference(provider, verification.ProviderEventId, payload, payment, logger, nowUtc);

        var transitionResult = ApplyPayload(payment, payload, metrics);
        if (transitionResult.IsFailure)
        {
            webhookEvent.MarkStale(payment.Id, transitionResult.Error.Code, DateTime.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "{Provider} webhook {EventType} ({ProviderEventId}) is stale for SaaSPayment {SaaSPaymentId}: {ErrorCode}.",
                provider,
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
            nameof(SaaSPayment),
            payment.Id,
            MapAuditAction(payment.Status),
            actorUserId: Guid.Empty,
            correlation.CorrelationId,
            before: (object?)null,
            after: new
            {
                payment.Status,
                Source = $"{provider}Webhook",
                verification.EventType,
            },
            reason: null,
            DateTime.UtcNow,
            ct
        );

        if (payment.Type == SaaSPaymentType.OnboardingInitial)
            await ProcessStripeWebhookHandler.PublishOnboardingResultAsync(payment, bus, correlation.CorrelationId, ct);

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "{Provider} webhook {EventType} ({ProviderEventId}) applied to SaaSPayment {SaaSPaymentId}: now {Status}.",
            provider,
            verification.EventType,
            verification.ProviderEventId,
            payment.Id,
            payment.Status
        );

        return Result.Success();
    }

    private static void ReconcileProviderReference(
        PaymentProviderCode provider,
        string providerEventId,
        WebhookEventPayload payload,
        SaaSPayment payment,
        ILogger<WebhookEvent> logger,
        DateTime nowUtc
    )
    {
        if (
            payload.ReconciledChargeReference is not { } reconciledRaw
            || reconciledRaw == payment.ExternalChargeReference?.Value
        )
            return;

        var reconciledReferenceResult = ExternalPaymentReference.Create(provider, reconciledRaw);
        if (reconciledReferenceResult.IsFailure)
        {
            logger.LogWarning(
                "{Provider} webhook {ProviderEventId} carried an invalid reconciled charge reference for SaaSPayment {SaaSPaymentId}: {ErrorCode}: {ErrorMessage}",
                provider,
                providerEventId,
                payment.Id,
                reconciledReferenceResult.Error.Code,
                reconciledReferenceResult.Error.Message
            );
            return;
        }

        var reconcileResult = payment.ReconcileProviderChargeReference(reconciledReferenceResult.Value, nowUtc);
        if (reconcileResult.IsFailure)
            logger.LogWarning(
                "{Provider} webhook {ProviderEventId} could not reconcile the charge reference for SaaSPayment {SaaSPaymentId}: {ErrorCode}: {ErrorMessage}",
                provider,
                providerEventId,
                payment.Id,
                reconcileResult.Error.Code,
                reconcileResult.Error.Message
            );
    }

    private static Result ApplyPayload(SaaSPayment payment, WebhookEventPayload payload, IPaymentAppMetrics metrics)
    {
        var nowUtc = DateTime.UtcNow;

        switch (payload.Status)
        {
            case PaymentStatus.Processing:
                return Result.Success();

            case PaymentStatus.Succeeded:
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
                var chargedBack = payment.MarkChargedBack(
                    nowUtc,
                    payload.FailureMessage ?? "Chargeback dispute created.",
                    Guid.Empty
                );
                if (chargedBack.IsFailure)
                    return chargedBack;
                metrics.RecordChargedBack(payment.ProviderCode.ToString());
                return Result.Success();

            default:
                return Result.Failure(
                    new Error(
                        "WebhookEvent.UnsupportedPaymentStatus",
                        $"Payment status {payload.Status} is not actionable."
                    )
                );
        }
    }

    private static Result ApplyRefund(
        SaaSPayment payment,
        long totalRefundedCents,
        DateTime nowUtc,
        IPaymentAppMetrics metrics
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

        metrics.RecordRefunded(payment.ProviderCode.ToString());
        return Result.Success();
    }

    private static PaymentAuditAction MapAuditAction(PaymentStatus status) =>
        status switch
        {
            PaymentStatus.Succeeded => PaymentAuditAction.SaaSPaymentSucceeded,
            PaymentStatus.Failed => PaymentAuditAction.SaaSPaymentFailed,
            PaymentStatus.Cancelled => PaymentAuditAction.SaaSPaymentCancelled,
            PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded => PaymentAuditAction.SaaSPaymentRefundedPartial,
            PaymentStatus.ChargedBack => PaymentAuditAction.SaaSPaymentChargedBack,
            _ => PaymentAuditAction.SaaSPaymentMarkedProcessing,
        };

    private static bool IsUnsupportedWebhookEvent(Error error) =>
        error.Code.EndsWith(".Webhook.UnsupportedEventType", StringComparison.Ordinal);

    private static string BuildSignatureSnapshot(
        PaymentProviderCode provider,
        IReadOnlyDictionary<string, string> headers
    ) =>
        provider switch
        {
            PaymentProviderCode.Stripe => GetHeader(headers, "Stripe-Signature") ?? string.Empty,
            PaymentProviderCode.PayPal => string.Join(
                ";",
                new[]
                {
                    "PAYPAL-AUTH-ALGO",
                    "PAYPAL-CERT-URL",
                    "PAYPAL-TRANSMISSION-ID",
                    "PAYPAL-TRANSMISSION-SIG",
                    "PAYPAL-TRANSMISSION-TIME",
                }.Select(name => $"{name}={GetHeader(headers, name) ?? string.Empty}")
            ),
            _ => string.Empty,
        };

    private static string? GetHeader(IReadOnlyDictionary<string, string> headers, string name)
    {
        if (headers.TryGetValue(name, out var value))
            return value;

        foreach (var (key, candidate) in headers)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return null;
    }
}
