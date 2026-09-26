using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.Common;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Domain.Webhooks;
using Wolverine;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.ProcessProviderWebhook;

public static class ProcessProviderWebhookHandler
{
    /// <summary>Webhook throttleado: la API responde 429 para que el provider lo reintente.</summary>
    public const string WebhookThrottledCode = "PaymentApp.WebhookThrottled";

    /// <summary>La ventana del throttle de webhooks (fija, de 1 minuto).</summary>
    public const int WebhookThrottleRetryAfterSeconds = 60;

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
        ITenantRegistry tenants,
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
            tenants,
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
        ITenantRegistry tenants,
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
        var nowUtc = DateTime.UtcNow;

        // Idempotencia STATUS-AWARE (F1): un evento en estado terminal ya fue resuelto → se descarta
        // como duplicado; uno NO terminal quedó a medias por un fallo transitorio (p.ej. crash tras
        // insertar la fila pero antes de aplicar el cargo) y esta nueva entrega del provider lo
        // re-procesa — así un pago real nunca se pierde por un reintento tratado como duplicado.
        var existing = await webhookEvents.GetByProviderEventIdAsync(provider, verification.ProviderEventId, ct);
        WebhookEvent webhookEvent;
        if (existing is not null)
        {
            if (existing.IsTerminal)
            {
                metrics.RecordWebhookDuplicate(provider.ToString());
                logger.LogInformation(
                    "{Provider} webhook {ProviderEventId} already {Status}; skipping (idempotent).",
                    provider,
                    verification.ProviderEventId,
                    existing.Status
                );
                return Result.Success();
            }

            var reprocessResult = existing.MarkReprocessing(nowUtc);
            if (reprocessResult.IsFailure)
                return Result.Failure(reprocessResult.Error);
            webhookEvent = existing;
            logger.LogWarning(
                "{Provider} webhook {ProviderEventId} was not applied on a previous delivery ({Status}); reprocessing.",
                provider,
                verification.ProviderEventId,
                existing.Status
            );
        }
        else
        {
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

            webhookEvent = receiveResult.Value;
            await webhookEvents.AddAsync(webhookEvent, ct);
            webhookEvent.MarkProcessing(nowUtc);
            try
            {
                await unitOfWork.SaveChangesAsync(ct);
            }
            catch (ConflictException ex) when (ex.Code == "Persistence.UniqueConstraint")
            {
                // Otra entrega concurrente insertó la fila primero — ella la está procesando.
                metrics.RecordWebhookDuplicate(provider.ToString());
                logger.LogInformation(
                    "{Provider} webhook {ProviderEventId} was inserted by a concurrent delivery; skipping (idempotent).",
                    provider,
                    verification.ProviderEventId
                );
                return Result.Success();
            }
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

        var throttleScope = WebhookThrottleScope(payment);
        if (await throttle.IsWebhookThrottledAsync(throttleScope, ct))
        {
            logger.LogWarning(
                "{Provider} webhook {ProviderEventId} throttled for scope {ThrottleScope}: too many webhook events in the last minute; the provider will retry it.",
                provider,
                verification.ProviderEventId,
                throttleScope
            );
            // Failed (no terminal) + 429: la próxima entrega del provider lo re-procesa. Antes quedaba
            // Rejected con 200 y el provider nunca reintentaba: el pago confirmado se perdía.
            webhookEvent.MarkFailed("Webhook rate exceeded; waiting for the provider retry.", DateTime.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Failure(new Error(WebhookThrottledCode, "Too many webhook events. Retry later."));
        }

        await throttle.RegisterWebhookAttemptAsync(throttleScope, ct);
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

        await SaaSPaymentResultPublisher.PublishAsync(payment, bus, correlation.CorrelationId, ct, tenants);

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

    // El pago de onboarding todavía no tiene tenant (TenantId vacío): sin esto todos los onboardings
    // compartían un único cupo y un pico de altas se throttleaba entre sí.
    private static Guid WebhookThrottleScope(SaaSPayment payment) =>
        payment.TenantId != Guid.Empty ? payment.TenantId : payment.OnboardingId ?? payment.Id;

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
                var amountMismatch = VerifyPaidAmount(payment, payload);
                if (amountMismatch is not null)
                    return Result.Failure(amountMismatch);
                return payment.MarkSucceeded(nowUtc, Guid.Empty);

            case PaymentStatus.Failed when payment.Status == PaymentStatus.Failed:
                // El cobro síncrono ya registró este fallo: reaplicarlo reprogramaría el dunning y
                // publicaría el resultado dos veces.
                return Result.Failure(
                    new Error("SaaSPayment.AlreadyFailed", "The payment is already marked as failed.")
                );

            case PaymentStatus.Failed:
                // Una renovación que falla de forma asíncrona sigue el mismo dunning que la síncrona.
                var nextRetryAtUtc = SaaSPaymentChargeOutcome.ComputeNextRetryAtUtc(
                    payment,
                    nowUtc,
                    failedAttemptRecorded: true
                );
                return payment.MarkFailed(
                    payload.FailureCode ?? "Provider.Unknown",
                    payload.FailureMessage ?? "The provider declined the charge.",
                    willRetry: nextRetryAtUtc is not null,
                    nextRetryAtUtc,
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

    // F2 defense-in-depth: aunque el evento de éxito esté autenticado por firma, se exige que el monto
    // cobrado que reporta el provider coincida con el cargo esperado (y la moneda). Si no coincide, NO
    // se aplica: el caller lo marca Stale con este código y el pago no queda como "Succeeded" por un
    // importe distinto (captura parcial / misconfig). Si el adapter no reporta monto (null) no se puede
    // verificar y se aplica igual — compatibilidad hacia atrás.
    private static Error? VerifyPaidAmount(SaaSPayment payment, WebhookEventPayload payload)
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
