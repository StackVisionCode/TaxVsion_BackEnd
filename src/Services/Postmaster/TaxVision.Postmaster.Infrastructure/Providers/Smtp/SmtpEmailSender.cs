using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Infrastructure.RateLimit;
using TaxVision.Postmaster.Infrastructure.Sending;

namespace TaxVision.Postmaster.Infrastructure.Providers.Smtp;

/// <summary>
/// Implementación SMTP de <see cref="IEmailSender"/> vía MailKit. El MIME lo arma
/// <see cref="MimeMessageBuilder"/> (Fase 3.5) con <c>LinkedResources</c> para logos CID. Transitorios
/// (4xx) reintentan con backoff exponencial vía Polly; rechazos de destinatario (5xx) se aíslan por
/// dirección y no abortan el envío al resto — ver <see cref="SendAsync"/>. Todo el intento (connect +
/// send) pasa por un <see cref="ProviderCircuitBreaker"/> propio por <c>ProviderCode</c> (Fase 9): tras
/// 5 fallos consecutivos deja de intentar contra ese proveedor por 60s.
/// </summary>
public sealed class SmtpEmailSender(ILogger<SmtpEmailSender> logger, ProviderCircuitBreakerRegistry circuitBreakers)
    : IEmailSender
{
    private static readonly ResiliencePipeline TransientRetryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(
            new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<SmtpCommandException>(IsTransientSmtpFailure),
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(1),
            }
        )
        .Build();

    public async Task<SendResult> SendAsync(
        SentMessage message,
        RenderedContent content,
        ResolvedEmailProvider provider,
        IReadOnlyList<InlineAssetBytes> inlineAssets,
        CancellationToken ct
    )
    {
        var mimeMessage = MimeMessageBuilder.Build(message, content, provider, inlineAssets);
        var breaker = circuitBreakers.GetOrCreate(provider.ProviderCode);

        try
        {
            return await breaker.ExecuteAsync(
                async token =>
                {
                    using var client = new global::MailKit.Net.Smtp.SmtpClient();
                    await ConnectAndAuthenticateAsync(client, provider, token);

                    var (accepted, rejected) = await SendWithPerRecipientRejectionHandlingAsync(
                        client,
                        mimeMessage,
                        message,
                        token
                    );
                    await client.DisconnectAsync(true, token);

                    if (accepted.Count == 0)
                        return new SendResult(
                            false,
                            null,
                            "All recipients were rejected by the SMTP server.",
                            rejected
                        );

                    var outcomes = accepted.Concat(rejected).ToList();
                    return new SendResult(true, mimeMessage.MessageId, null, outcomes);
                },
                ct
            );
        }
        catch (BrokenCircuitException)
        {
            logger.LogWarning(
                "Circuit breaker open for provider {ProviderCode} — send skipped for SentMessage {SentMessageId}.",
                provider.ProviderCode,
                message.Id
            );
            return new SendResult(false, null, $"Provider '{provider.ProviderCode}' circuit breaker is open.", []);
        }
        catch (Exception ex) when (ex is SmtpCommandException or SmtpProtocolException or AuthenticationException)
        {
            logger.LogWarning(
                ex,
                "SMTP send failed for SentMessage {SentMessageId} via {Host}.",
                message.Id,
                provider.Host
            );
            return new SendResult(false, null, ex.Message, []);
        }
    }

    private static async Task ConnectAndAuthenticateAsync(
        global::MailKit.Net.Smtp.SmtpClient client,
        ResolvedEmailProvider provider,
        CancellationToken ct
    )
    {
        var socketOptions = provider.UseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
        await TransientRetryPipeline.ExecuteAsync(
            async token => await client.ConnectAsync(provider.Host, provider.Port, socketOptions, token),
            ct
        );

        if (!string.IsNullOrWhiteSpace(provider.Username) && provider.Password is not null)
            await client.AuthenticateAsync(provider.Username, provider.Password, ct);
    }

    /// <summary>
    /// Envía el envelope completo; si el servidor rechaza destinatarios individuales (5xx), los
    /// aísla en <c>rejected</c> y reintenta una sola vez con el resto (nunca recursivo/ilimitado).
    /// </summary>
    private async Task<(
        List<RecipientSendOutcome> accepted,
        List<RecipientSendOutcome> rejected
    )> SendWithPerRecipientRejectionHandlingAsync(
        global::MailKit.Net.Smtp.SmtpClient client,
        MimeMessage mimeMessage,
        SentMessage message,
        CancellationToken ct
    )
    {
        try
        {
            await TransientRetryPipeline.ExecuteAsync(async token => await client.SendAsync(mimeMessage, token), ct);
            return (BuildAcceptedOutcomes(message, excludedAddresses: []), []);
        }
        catch (SmtpCommandException ex) when (ex.ErrorCode == SmtpErrorCode.RecipientNotAccepted)
        {
            var rejectedAddress = ex.Mailbox?.Address;
            logger.LogWarning(ex, "Recipient {Address} rejected by SMTP server, retrying without it.", rejectedAddress);

            RemoveRecipient(mimeMessage, rejectedAddress);
            await TransientRetryPipeline.ExecuteAsync(async token => await client.SendAsync(mimeMessage, token), ct);

            var excluded = rejectedAddress is null ? [] : new HashSet<string> { rejectedAddress };
            var rejected = BuildRejectedOutcomes(message, excluded, ex.Message);
            return (BuildAcceptedOutcomes(message, excluded), rejected);
        }
    }

    private static void RemoveRecipient(MimeMessage mimeMessage, string? address)
    {
        if (address is null)
            return;

        RemoveMatchingMailbox(mimeMessage.To, address);
        RemoveMatchingMailbox(mimeMessage.Cc, address);
        RemoveMatchingMailbox(mimeMessage.Bcc, address);
    }

    private static void RemoveMatchingMailbox(InternetAddressList list, string address)
    {
        var match = list.Mailboxes.FirstOrDefault(m => m.Address == address);
        if (match is not null)
            list.Remove(match);
    }

    private static List<RecipientSendOutcome> BuildAcceptedOutcomes(
        SentMessage message,
        ICollection<string> excludedAddresses
    ) =>
        message
            .Recipients.Where(r => !excludedAddresses.Contains(r.Address))
            .Select(r => new RecipientSendOutcome(r.Id, r.Address, RecipientSendStatus.Sent, null))
            .ToList();

    private static List<RecipientSendOutcome> BuildRejectedOutcomes(
        SentMessage message,
        ICollection<string> rejectedAddresses,
        string reason
    ) =>
        message
            .Recipients.Where(r => rejectedAddresses.Contains(r.Address))
            .Select(r => new RecipientSendOutcome(r.Id, r.Address, RecipientSendStatus.Rejected, reason))
            .ToList();

    private static bool IsTransientSmtpFailure(SmtpCommandException ex) =>
        ex.ErrorCode is not SmtpErrorCode.RecipientNotAccepted and not SmtpErrorCode.SenderNotAccepted
        && (int)ex.StatusCode is >= 400 and < 500;
}
