using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Domain.Connect;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Domain.Webhooks;

namespace TaxVision.PaymentClient.Application.TenantConnect.Commands.ProcessConnectWebhook;

/// <summary>Registra el evento. <paramref name="Event"/> null: el webhook termina acá con <paramref name="Finished"/>.</summary>
internal sealed record ConnectWebhookRegistration(WebhookEvent? Event, Result? Finished = null)
{
    /// <summary>Idempotencia STATUS-AWARE (F1): un evento terminal ya resuelto se descarta como duplicado; uno no
    /// terminal (Failed) quedó a medias por un fallo transitorio y se re-procesa.</summary>
    public static async Task<ConnectWebhookRegistration> RegisterAsync(
        ConnectWebhookEvent evt,
        TenantConnectAccount connectAccount,
        ProcessConnectWebhookCommand command,
        IWebhookEventRepository webhookEvents,
        ILogger logger,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var existing = await webhookEvents.GetByProviderEventIdAsync(
            connectAccount.TenantId,
            PaymentProviderCode.Stripe,
            evt.ProviderEventId,
            ct
        );
        if (existing is not null)
        {
            if (existing.IsTerminal)
            {
                logger.LogInformation(
                    "Stripe Connect webhook {ProviderEventId} already {Status}; skipping (idempotent).",
                    evt.ProviderEventId,
                    existing.Status
                );
                return new ConnectWebhookRegistration(null, Result.Success());
            }

            var reprocessResult = existing.MarkReprocessing(nowUtc);
            if (reprocessResult.IsFailure)
                return new ConnectWebhookRegistration(null, Result.Failure(reprocessResult.Error));

            logger.LogWarning(
                "Stripe Connect webhook {ProviderEventId} was not applied on a previous delivery ({Status}); reprocessing.",
                evt.ProviderEventId,
                existing.Status
            );
            return new ConnectWebhookRegistration(existing);
        }

        var receiveResult = WebhookEvent.Receive(
            connectAccount.TenantId,
            PaymentProviderCode.Stripe,
            evt.ProviderEventId,
            evt.EventType,
            command.RawPayload,
            command.SignatureHeader,
            nowUtc
        );
        if (receiveResult.IsFailure)
            return new ConnectWebhookRegistration(null, Result.Failure(receiveResult.Error));

        var webhookEvent = receiveResult.Value;
        await webhookEvents.AddAsync(webhookEvent, ct);
        webhookEvent.MarkProcessing(nowUtc);
        return new ConnectWebhookRegistration(webhookEvent);
    }
}
