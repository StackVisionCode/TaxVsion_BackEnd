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
        ILogger<WebhookEvent> logger,
        CancellationToken ct
    )
    {
        var intake = await ProviderWebhookIntake.VerifyAndRegisterAsync(
            provider,
            rawPayload,
            headers,
            providerFactory,
            webhookSecrets,
            webhookEvents,
            unitOfWork,
            metrics,
            logger,
            ct
        );
        if (intake.Webhook is not { } webhook)
            return intake.Finished!;

        var resolved = await WebhookPaymentResolver.ResolveAsync(
            provider,
            rawPayload,
            webhook,
            payments,
            throttle,
            unitOfWork,
            logger,
            ct
        );
        if (resolved is not { Payload: { } payload, Payment: { } payment })
            return resolved.Finished!;

        var webhookEvent = webhook.Event;
        var verification = webhook.Verification;
        WebhookPayloadApplier.ReconcileProviderReference(
            provider,
            verification.ProviderEventId,
            payload,
            payment,
            logger,
            webhook.ReceivedAtUtc
        );

        var transitionResult = WebhookPayloadApplier.Apply(payment, payload, metrics);
        if (transitionResult.IsFailure)
        {
            await WebhookPayloadApplier.RecordNotAppliedAsync(
                payment,
                webhookEvent,
                payload,
                transitionResult.Error,
                audit,
                metrics,
                correlation,
                unitOfWork,
                logger,
                ct
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
            WebhookPayloadApplier.AuditActionFor(payment.Status),
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

        // El resultado lo avisa SaaSPaymentDomainEventHandler con el commit (evento de dominio).
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
}
