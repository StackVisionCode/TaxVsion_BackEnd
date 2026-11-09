using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.ProcessProviderWebhook;

/// <summary><paramref name="Finished"/> con valor: no hay nada que aplicar y el webhook termina con ese resultado.</summary>
internal sealed record ResolvedWebhookPayment(
    WebhookEventPayload? Payload,
    SaaSPayment? Payment,
    Result? Finished = null
);

/// <summary>Segundo paso del webhook: lee el payload, encuentra el pago y aplica el throttle.</summary>
internal static class WebhookPaymentResolver
{
    public static async Task<ResolvedWebhookPayment> ResolveAsync(
        PaymentProviderCode provider,
        string rawPayload,
        VerifiedWebhook webhook,
        ISaaSPaymentRepository payments,
        IPaymentAttemptThrottle throttle,
        IUnitOfWork unitOfWork,
        ILogger logger,
        CancellationToken ct
    )
    {
        var webhookEvent = webhook.Event;
        var providerEventId = webhook.Verification.ProviderEventId;

        var payloadResult = await webhook.Adapter.ParseWebhookEventAsync(
            rawPayload,
            webhook.Verification.EventType,
            ct
        );
        if (payloadResult.IsFailure)
        {
            if (IsUnsupportedWebhookEvent(payloadResult.Error))
            {
                webhookEvent.MarkRejected(payloadResult.Error.Message, DateTime.UtcNow);
                await unitOfWork.SaveChangesAsync(ct);
                return Finish(Result.Success());
            }

            webhookEvent.MarkFailed(payloadResult.Error.Message, DateTime.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            return Finish(Result.Failure(payloadResult.Error));
        }

        var payload = payloadResult.Value;
        var payment = await payments.GetByExternalReferenceAsync(provider, payload.ProviderChargeReference, ct);
        if (payment is null)
        {
            logger.LogWarning(
                "{Provider} webhook {ProviderEventId} references unknown charge {Reference}; rejecting.",
                provider,
                providerEventId,
                payload.ProviderChargeReference
            );
            webhookEvent.MarkRejected("No matching SaaSPayment for this charge reference.", DateTime.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            return Finish(Result.Success());
        }

        var throttleScope = WebhookThrottleScope(payment);
        if (await throttle.IsWebhookThrottledAsync(throttleScope, ct))
        {
            logger.LogWarning(
                "{Provider} webhook {ProviderEventId} throttled for scope {ThrottleScope}: too many webhook events in the last minute; the provider will retry it.",
                provider,
                providerEventId,
                throttleScope
            );
            // Failed (no terminal) + 429: la próxima entrega del provider lo re-procesa. Antes quedaba
            // Rejected con 200 y el provider nunca reintentaba: el pago confirmado se perdía.
            webhookEvent.MarkFailed("Webhook rate exceeded; waiting for the provider retry.", DateTime.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            return Finish(
                Result.Failure(
                    new Error(
                        ProcessProviderWebhookHandler.WebhookThrottledCode,
                        "Too many webhook events. Retry later."
                    )
                )
            );
        }

        await throttle.RegisterWebhookAttemptAsync(throttleScope, ct);
        return new ResolvedWebhookPayment(payload, payment);
    }

    private static ResolvedWebhookPayment Finish(Result result) => new(null, null, result);

    // El pago de onboarding todavía no tiene tenant (TenantId vacío): sin esto todos los onboardings
    // compartían un único cupo y un pico de altas se throttleaba entre sí.
    private static Guid WebhookThrottleScope(SaaSPayment payment) =>
        payment.TenantId != Guid.Empty ? payment.TenantId : payment.OnboardingId ?? payment.Id;

    private static bool IsUnsupportedWebhookEvent(Error error) =>
        error.Code.EndsWith(".Webhook.UnsupportedEventType", StringComparison.Ordinal);
}
