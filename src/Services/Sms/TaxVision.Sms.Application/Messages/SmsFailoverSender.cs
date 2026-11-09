using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Sms.Application.Messages.Commands;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Domain;

namespace TaxVision.Sms.Application.Messages;

/// <summary>Lo que quedó de un envío. <paramref name="Provider"/> es null si ninguno lo aceptó.</summary>
public sealed record SmsSendAttempt(
    SmsSendOutcome Outcome,
    ISmsProvider? Provider,
    string? ProviderMessageId,
    Error? Error
);

/// <summary>Fase 2 del envío: recorre los proveedores de plataforma según <see cref="SmsFailoverPolicy"/>.</summary>
public static class SmsFailoverSender
{
    public static async Task<SmsSendAttempt> SendAsync(
        IReadOnlyList<ISmsProvider> providers,
        SmsSendRequest request,
        ILogger logger,
        CancellationToken ct
    )
    {
        Error? lastError = null;
        foreach (var provider in providers)
        {
            var mediaError = SmsMediaValidator.Validate(provider.Capabilities, request.Media);
            if (mediaError is not null)
            {
                lastError = mediaError;
                logger.LogWarning(
                    "SMS provider {Provider} cannot carry the media for {To} ({Code}); trying next.",
                    provider.Code,
                    request.To,
                    mediaError.Code
                );
                continue;
            }

            SmsSendResult result;
            try
            {
                var sent = await provider.SendAsync(request, ct);
                result = sent.IsSuccess
                    ? sent.Value
                    : new SmsSendResult(false, null, sent.Error.Code, sent.Error.Message);
            }
            catch (Exception ex)
            {
                // Un adapter que lanza pudo haber enviado: resultado desconocido, sin failover.
                logger.LogWarning(
                    ex,
                    "SMS provider {Provider} threw sending to {To}; outcome unknown.",
                    provider.Code,
                    request.To
                );
                return new SmsSendAttempt(
                    SmsSendOutcome.OutcomeUnknown,
                    provider,
                    null,
                    SmsErrors.ProviderOutcomeUnknown
                );
            }

            if (result.Accepted)
                return new SmsSendAttempt(SmsSendOutcome.Accepted, provider, result.ProviderMessageId, null);

            lastError = new Error(
                result.ErrorCode ?? SmsErrors.ProviderRejected.Code,
                result.ErrorMessage ?? string.Empty
            );
            if (!SmsFailoverPolicy.ShouldTryNext(result))
                return lastError.Code == SmsSendFailure.OutcomeUnknown
                    ? new SmsSendAttempt(SmsSendOutcome.OutcomeUnknown, provider, result.ProviderMessageId, lastError)
                    : new SmsSendAttempt(SmsSendOutcome.RecipientOptedOut, provider, null, lastError);

            logger.LogWarning(
                "SMS provider {Provider} did not accept {To} ({Code}); trying next if any.",
                provider.Code,
                request.To,
                lastError.Code
            );
        }

        return new SmsSendAttempt(SmsSendOutcome.Rejected, null, null, lastError ?? SmsErrors.ProviderRejected);
    }
}
