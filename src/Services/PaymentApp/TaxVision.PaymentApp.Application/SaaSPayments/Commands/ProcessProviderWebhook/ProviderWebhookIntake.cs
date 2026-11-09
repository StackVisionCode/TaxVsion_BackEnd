using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Domain.Webhooks;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.ProcessProviderWebhook;

/// <summary>Webhook verificado y registrado, listo para resolver su pago.</summary>
internal sealed record VerifiedWebhook(
    IPaymentProvider Adapter,
    WebhookVerificationResult Verification,
    WebhookEvent Event,
    DateTime ReceivedAtUtc
);

/// <summary><paramref name="Finished"/> con valor: el webhook terminó acá, con ese resultado.</summary>
internal sealed record WebhookIntakeResult(VerifiedWebhook? Webhook, Result? Finished = null);

/// <summary>Primer paso del webhook: verifica la firma y registra el evento con su idempotencia.</summary>
internal static class ProviderWebhookIntake
{
    public static async Task<WebhookIntakeResult> VerifyAndRegisterAsync(
        PaymentProviderCode provider,
        string rawPayload,
        IReadOnlyDictionary<string, string> headers,
        IPaymentAdapterFactory providerFactory,
        IProviderWebhookSecrets webhookSecrets,
        IWebhookEventRepository webhookEvents,
        IUnitOfWork unitOfWork,
        IPaymentAppMetrics metrics,
        ILogger logger,
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
            return Finish(
                Result.Failure(
                    new Error("PaymentProvider.NotConfigured", "The selected payment provider is not configured.")
                )
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
            return Finish(Result.Failure(verificationResult.Error));
        }

        var verification = verificationResult.Value;
        var nowUtc = DateTime.UtcNow;

        // Idempotencia STATUS-AWARE (F1): un evento en estado terminal ya fue resuelto → se descarta
        // como duplicado; uno NO terminal quedó a medias por un fallo transitorio (p.ej. crash tras
        // insertar la fila pero antes de aplicar el cargo) y esta nueva entrega del provider lo
        // re-procesa — así un pago real nunca se pierde por un reintento tratado como duplicado.
        var existing = await webhookEvents.GetByProviderEventIdAsync(provider, verification.ProviderEventId, ct);
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
                return Finish(Result.Success());
            }

            var reprocessResult = existing.MarkReprocessing(nowUtc);
            if (reprocessResult.IsFailure)
                return Finish(Result.Failure(reprocessResult.Error));

            logger.LogWarning(
                "{Provider} webhook {ProviderEventId} was not applied on a previous delivery ({Status}); reprocessing.",
                provider,
                verification.ProviderEventId,
                existing.Status
            );
            return new WebhookIntakeResult(new VerifiedWebhook(adapter, verification, existing, nowUtc));
        }

        var receiveResult = WebhookEvent.Receive(
            provider,
            verification.ProviderEventId,
            verification.EventType,
            rawPayload,
            BuildSignatureSnapshot(provider, headers),
            nowUtc
        );
        if (receiveResult.IsFailure)
            return Finish(Result.Failure(receiveResult.Error));

        var webhookEvent = receiveResult.Value;
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
            return Finish(Result.Success());
        }

        return new WebhookIntakeResult(new VerifiedWebhook(adapter, verification, webhookEvent, nowUtc));
    }

    private static WebhookIntakeResult Finish(Result result) => new(null, result);

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
