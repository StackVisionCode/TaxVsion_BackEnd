using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.Common;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Domain.Webhooks;
using Wolverine;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.ProcessStripeWebhook;

/// <summary>
/// Signature verification obligatoria (§23/B.3 del plan) — este handler es el único lugar
/// donde un payload de Stripe se trata como confiable, y solo después de
/// <see cref="IPaymentProvider.VerifyWebhookSignatureAsync"/>. Dedupe por
/// <c>(ProviderCode, ProviderEventId)</c> antes de mutar nada — un reintento de Stripe del
/// mismo evento es un no-op idempotente.
/// </summary>
public static class ProcessStripeWebhookHandler
{
    public static async Task<Result> Handle(
        ProcessStripeWebhookCommand command,
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
        metrics.RecordWebhookReceived(PaymentProviderCode.Stripe.ToString());

        var adapter = providerFactory.Resolve(PaymentProviderCode.Stripe);
        var secret = webhookSecrets.GetWebhookSecret(PaymentProviderCode.Stripe);
        if (string.IsNullOrWhiteSpace(secret))
            return Result.Failure(
                new Error("Stripe.WebhookSecret.Missing", "Stripe webhook secret is not configured.")
            );

        var verificationResult = await adapter.VerifyWebhookSignatureAsync(
            command.RawPayload,
            command.SignatureHeader,
            secret,
            ct
        );
        if (verificationResult.IsFailure)
        {
            metrics.RecordWebhookSignatureFailed(PaymentProviderCode.Stripe.ToString());
            logger.LogWarning(
                "Rejected Stripe webhook with invalid signature: {Error}",
                verificationResult.Error.Message
            );
            return Result.Failure(verificationResult.Error);
        }

        var verification = verificationResult.Value;
        var alreadyReceived = await webhookEvents.ExistsAsync(
            PaymentProviderCode.Stripe,
            verification.ProviderEventId,
            ct
        );
        if (alreadyReceived)
        {
            metrics.RecordWebhookDuplicate(PaymentProviderCode.Stripe.ToString());
            logger.LogInformation(
                "Stripe webhook {ProviderEventId} already processed; skipping (idempotent).",
                verification.ProviderEventId
            );
            return Result.Success();
        }

        var nowUtc = DateTime.UtcNow;
        var receiveResult = WebhookEvent.Receive(
            PaymentProviderCode.Stripe,
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
            PaymentProviderCode.Stripe,
            payload.ProviderChargeReference,
            ct
        );
        if (payment is null)
        {
            logger.LogWarning(
                "Stripe webhook {ProviderEventId} references unknown charge {Reference}; rejecting.",
                verification.ProviderEventId,
                payload.ProviderChargeReference
            );
            webhookEvent.MarkRejected("No matching SaaSPayment for this charge reference.", DateTime.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success();
        }

        // Defensa en profundidad (§41.4) — un tenant recibiendo webhooks muy por encima de lo
        // normal probablemente sea un provider en bucle de reintento roto, no tráfico legítimo.
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

        // PayFlow — checkout.session.completed trae el PaymentIntent real cuando la sesión se
        // creó sin uno sincrónico (comportamiento documentado de Stripe, ver StripePaymentAdapter).
        // Reconciliar acá, antes de aplicar la transición de estado, para que refund/dispute
        // futuros (que referencian el PaymentIntent, no la Session) puedan resolver este pago.
        if (
            payload.ReconciledChargeReference is { } reconciledRaw
            && reconciledRaw != payment.ExternalChargeReference?.Value
        )
        {
            var reconciledReferenceResult = ExternalPaymentReference.Create(PaymentProviderCode.Stripe, reconciledRaw);
            if (reconciledReferenceResult.IsFailure)
                logger.LogWarning(
                    "Stripe webhook {ProviderEventId} carried an invalid reconciled charge reference for SaaSPayment {SaaSPaymentId}: {ErrorCode}: {ErrorMessage}",
                    verification.ProviderEventId,
                    payment.Id,
                    reconciledReferenceResult.Error.Code,
                    reconciledReferenceResult.Error.Message
                );
            else
            {
                var reconcileResult = payment.ReconcileProviderChargeReference(reconciledReferenceResult.Value, nowUtc);
                if (reconcileResult.IsFailure)
                    logger.LogWarning(
                        "Stripe webhook {ProviderEventId} could not reconcile the charge reference for SaaSPayment {SaaSPaymentId}: {ErrorCode}: {ErrorMessage}",
                        verification.ProviderEventId,
                        payment.Id,
                        reconcileResult.Error.Code,
                        reconcileResult.Error.Message
                    );
            }
        }

        var transitionResult = ApplyPayload(payment, payload, metrics);
        if (transitionResult.IsFailure)
        {
            webhookEvent.MarkStale(payment.Id, transitionResult.Error.Code, DateTime.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "Stripe webhook {EventType} ({ProviderEventId}) is stale for SaaSPayment {SaaSPaymentId}: {ErrorCode}.",
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
                Source = "StripeWebhook",
                verification.EventType,
            },
            reason: null,
            DateTime.UtcNow,
            ct
        );

        // PayFlow (Fase 8) — el pago de un onboarding pago-primero recién llegó a un estado
        // terminal por webhook (nunca por ChargeSaaSPaymentHandler, que este flujo no usa).
        // payment.Type/payment.OnboardingId ya identifican que es de onboarding sin necesidad
        // de leer metadata cruda de Stripe — se determina completamente desde el propio
        // aggregate ya cargado por GetByExternalReferenceAsync.
        if (payment.Type == SaaSPaymentType.OnboardingInitial)
            await PublishOnboardingResultAsync(payment, bus, correlation.CorrelationId, ct);

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Stripe webhook {EventType} ({ProviderEventId}) applied to SaaSPayment {SaaSPaymentId}: now {Status}.",
            verification.EventType,
            verification.ProviderEventId,
            payment.Id,
            payment.Status
        );

        return Result.Success();
    }

    private static Result ApplyPayload(SaaSPayment payment, WebhookEventPayload payload, IPaymentAppMetrics metrics)
    {
        var nowUtc = DateTime.UtcNow;

        switch (payload.Status)
        {
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

    /// <summary><paramref name="totalRefundedCents"/> es el acumulado en el charge de
    /// Stripe, no el delta — se resta lo ya registrado localmente para no duplicar. Solo
    /// registra <c>refunded_total</c> si esto es una novedad para nosotros — un refund ya
    /// iniciado por <c>RefundSaaSPaymentHandler</c> (admin) ya lo contó ahí, esto evita
    /// contarlo dos veces cuando Stripe confirma por webhook lo que ya sabíamos.</summary>
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

        var refundResult = payment.RefundPartial(deltaMoney.Value, "Refunded via Stripe webhook.", Guid.Empty, nowUtc);
        if (refundResult.IsFailure)
            return refundResult;

        metrics.RecordRefunded(payment.ProviderCode.ToString());
        return Result.Success();
    }

    /// <summary>PayFlow (Fase 8) — no-op si el pago no llegó a Succeeded/Failed en esta
    /// aplicación de webhook (p.ej. quedó Refunded/ChargedBack en un evento posterior, que no
    /// aplica a un pago inicial de onboarding en la práctica pero no está prohibido por la
    /// máquina de estados). Público porque <c>PendingChargeReconciliationJob</c> también resuelve
    /// pagos de onboarding fuera del webhook (fallback out-of-band) y debe publicar el mismo
    /// evento -- de lo contrario el Saga de Auth queda esperando un evento que nunca llega si el
    /// job resuelve el pago antes de que el webhook logre procesarse.</summary>
    public static async ValueTask PublishOnboardingResultAsync(
        SaaSPayment payment,
        IMessageBus bus,
        string correlationId,
        CancellationToken ct
    )
    {
        if (payment.Status == PaymentStatus.Succeeded)
        {
            await bus.PublishAsync(
                new OnboardingPaymentSucceededIntegrationEvent
                {
                    TenantId = PlatformTenant.Id,
                    OnboardingId = payment.OnboardingId!.Value,
                    SaaSPaymentId = payment.Id,
                    PlanId = payment.TargetAggregateId,
                    AmountPaidCents = payment.Amount.AmountCents,
                    Currency = payment.Amount.Currency,
                    PaidAtUtc = payment.PaidAtUtc ?? DateTime.UtcNow,
                    ProviderPaymentReference = payment.ExternalChargeReference?.Value ?? string.Empty,
                    CorrelationId = correlationId,
                }
            );
        }
        else if (payment.Status == PaymentStatus.Failed)
        {
            await bus.PublishAsync(
                new OnboardingPaymentFailedIntegrationEvent
                {
                    TenantId = PlatformTenant.Id,
                    OnboardingId = payment.OnboardingId!.Value,
                    SaaSPaymentId = payment.Id,
                    FailureCode = payment.FailureCode ?? "Unknown",
                    FailureReason = payment.FailureReason ?? "The charge failed.",
                    CorrelationId = correlationId,
                }
            );
        }
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
}
