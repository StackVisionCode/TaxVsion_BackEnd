using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using MimeKit.Utils;
using Polly.CircuitBreaker;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.Observability;
using TaxVision.Connectors.Infrastructure.RateLimit;

namespace TaxVision.Connectors.Infrastructure.Providers.Manual;

/// <summary>
/// Envío por SMTP manual (MailKit, nunca <c>System.Net.Mail.SmtpClient</c> — ver auditoría legacy en
/// D3 Compose §5) — la contraparte de envío de <see cref="TaxVision.Connectors.Infrastructure.Providers.Imap.ImapClient"/>
/// (que solo lee). El circuit breaker es por-<c>AccountId</c>, no por-proveedor como Gmail/Graph: cada
/// cuenta manual apunta a un servidor SMTP distinto, así que el estado de salud de uno no debe afectar
/// a otro (D3 Compose §11.1).
/// </summary>
public sealed class SmtpManualClient(
    ISmtpCredentialsRepository credentialsRepository,
    IEncryptedSecretProtector protector,
    ProviderCircuitBreakerRegistry circuitBreakers,
    ILogger<SmtpManualClient> logger
) : IOutboundEmailProviderClient
{
    public ProviderCode ProviderCode => ProviderCode.Imap;

    public async Task<SendMessageResult> SendMessageAsync(
        Guid accountId,
        string fromAddress,
        string? fromDisplayName,
        OutboundMessage message,
        CancellationToken ct = default
    )
    {
        var credentialsResult = await credentialsRepository.GetByAccountIdAsync(accountId, ct);
        if (credentialsResult.IsFailure)
            throw new OutboundEmailSendException(
                SendFailureReason.InvalidRequest,
                $"SMTP send credentials are not configured for this account: {credentialsResult.Error.Message}"
            );
        var credentials = credentialsResult.Value;

        MimeMessage mime;
        try
        {
            mime = BuildMimeMessage(fromAddress, fromDisplayName, message);
        }
        catch (ParseException ex)
        {
            throw new OutboundEmailSendException(
                SendFailureReason.InvalidRequest,
                $"Invalid email address in outbound message: {ex.Message}",
                ex
            );
        }

        var breaker = circuitBreakers.GetOrCreate($"SmtpManual:{accountId}");
        try
        {
            await breaker.ExecuteAsync(token => SendViaSmtpAsync(credentials, mime, token), ct);
        }
        catch (BrokenCircuitException ex)
        {
            RecordSendFailureMetric(SendFailureReason.TransientProviderError);
            throw new OutboundEmailSendException(
                SendFailureReason.TransientProviderError,
                "SMTP circuit breaker is open — too many recent failures for this account.",
                ex
            );
        }
        catch (OutboundEmailSendException)
        {
            throw;
        }

        ConnectorsMetrics.MessagesSent.Add(1, new KeyValuePair<string, object?>("provider", "SmtpManual"));
        return new SendMessageResult(mime.MessageId, ProviderThreadId: null, DateTime.UtcNow);
    }

    /// <summary>Connect+authenticate+send es la única superficie que puede fallar de verdad acá — todo lo demás (MIME) ya se validó antes de entrar al breaker.</summary>
    private async Task<bool> SendViaSmtpAsync(SmtpCredentials credentials, MimeMessage mime, CancellationToken ct)
    {
        var password = credentials.PasswordCipher.Decrypt(protector);
        using var client = new SmtpClient();
        try
        {
            var socketOptions = credentials.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
            await client.ConnectAsync(credentials.Host, credentials.Port, socketOptions, ct);
            await client.AuthenticateAsync(credentials.Username, password, ct);
            await client.SendAsync(mime, ct);
            await client.DisconnectAsync(true, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutboundEmailSendException)
        {
            var reason = ClassifyFailure(ex);
            RecordSendFailureMetric(reason);
            logger.LogWarning(ex, "SMTP manual send failed for account {AccountId}.", credentials.AccountId);
            throw new OutboundEmailSendException(reason, $"SMTP send failed: {ex.Message}", ex);
        }
    }

    private static SendFailureReason ClassifyFailure(Exception ex) =>
        ex switch
        {
            AuthenticationException => SendFailureReason.PermissionDenied,
            SmtpCommandException { StatusCode: var code } when (int)code is >= 500 and < 600 =>
                SendFailureReason.InvalidRequest,
            SmtpCommandException => SendFailureReason.TransientProviderError,
            _ => SendFailureReason.TransientProviderError,
        };

    private static void RecordSendFailureMetric(SendFailureReason reason) =>
        ConnectorsMetrics.SendFailures.Add(
            1,
            new KeyValuePair<string, object?>("provider", "SmtpManual"),
            new KeyValuePair<string, object?>("reason", reason.ToString())
        );

    /// <summary>Sin threadId nativo (SMTP no tiene uno) — solo headers RFC 2822/5322 References/In-Reply-To, igual que cualquier MUA estándar.</summary>
    private static MimeMessage BuildMimeMessage(string fromAddress, string? fromDisplayName, OutboundMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(fromDisplayName ?? string.Empty, fromAddress));

        var atIndex = fromAddress.IndexOf('@');
        var fromDomain = atIndex >= 0 ? fromAddress[(atIndex + 1)..] : "taxvision.local";
        mime.MessageId = MimeUtils.GenerateMessageId(fromDomain);

        foreach (var to in message.To)
            mime.To.Add(MailboxAddress.Parse(to));
        foreach (var cc in message.Cc)
            mime.Cc.Add(MailboxAddress.Parse(cc));
        foreach (var bcc in message.Bcc)
            mime.Bcc.Add(MailboxAddress.Parse(bcc));
        if (!string.IsNullOrWhiteSpace(message.ReplyToDisplayAddress))
            mime.ReplyTo.Add(MailboxAddress.Parse(message.ReplyToDisplayAddress));

        mime.Subject = message.Subject;

        if (!string.IsNullOrWhiteSpace(message.InReplyToInternetMessageId))
            mime.InReplyTo = message.InReplyToInternetMessageId;
        foreach (var reference in message.References ?? [])
            mime.References.Add(reference);

        var bodyBuilder = new BodyBuilder { HtmlBody = message.Html, TextBody = message.Text };
        foreach (var attachment in message.Attachments)
            bodyBuilder.Attachments.Add(
                attachment.Filename,
                attachment.Content,
                ContentType.Parse(attachment.ContentType)
            );

        mime.Body = bodyBuilder.ToMessageBody();
        return mime;
    }
}
