using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using TaxVision.Notification.Application.Email.Sending.Campaign;

namespace TaxVision.Notification.Infrastructure.Email.Campaign;

/// <summary>
/// Adapter SMTP de <see cref="ICampaignEmailProvider"/> (MailKit). Envía el correo de campaña DIRECTO por
/// un relay SMTP (SMTP2GO: <c>mail.smtp2go.com:2525</c> STARTTLS) usando la config de
/// <see cref="CampaignEmailOptions.Smtp"/> — sin pasar por Postmaster ni por su resolución de proveedor.
/// Un fallo de transporte/credenciales/rechazo vuelve como <see cref="CampaignEmailSendResult.Fail"/> con
/// un código estable; nunca lanza (el consumer lo mapea a Failed y sigue con el resto de la audiencia).
/// </summary>
public sealed class SmtpCampaignEmailProvider(
    IOptions<CampaignEmailOptions> options,
    ICampaignEmailThrottle throttle,
    ILogger<SmtpCampaignEmailProvider> logger
) : ICampaignEmailProvider
{
    public string Code => options.Value.Provider;

    public async Task<CampaignEmailSendResult> SendAsync(CampaignEmailMessage message, CancellationToken ct = default)
    {
        var smtp = options.Value.Smtp;
        if (string.IsNullOrWhiteSpace(smtp.Host) || string.IsNullOrWhiteSpace(smtp.FromAddress))
        {
            logger.LogError("CampaignEmail SMTP provider is not configured (Host/FromAddress missing).");
            return CampaignEmailSendResult.Fail("email.providerNotConfigured");
        }

        await throttle.WaitTurnAsync(ct);

        MimeMessage mime;
        try
        {
            mime = BuildMime(smtp, message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "CampaignEmail: invalid message for {To} (dispatch {DispatchId}).", message.To, message.DispatchId);
            return CampaignEmailSendResult.Fail("email.invalidMessage");
        }

        try
        {
            using var client = new SmtpClient();
            var secure = smtp.UseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
            await client.ConnectAsync(smtp.Host, smtp.Port, secure, ct);
            if (!string.IsNullOrWhiteSpace(smtp.Username))
                await client.AuthenticateAsync(smtp.Username, smtp.Password ?? string.Empty, ct);
            var serverResponse = await client.SendAsync(mime, ct);
            await client.DisconnectAsync(true, ct);
            // El messageId MIME es nuestro ref estable para correlacionar (el relay no devuelve uno propio aquí).
            return CampaignEmailSendResult.Ok(mime.MessageId ?? serverResponse);
        }
        catch (AuthenticationException ex)
        {
            logger.LogError(ex, "CampaignEmail SMTP auth failed for provider {Provider}.", Code);
            return CampaignEmailSendResult.Fail("email.authFailed");
        }
        catch (SmtpCommandException ex)
        {
            logger.LogWarning(ex, "CampaignEmail SMTP rejected {To} ({Status}) via {Provider}.", message.To, ex.StatusCode, Code);
            return CampaignEmailSendResult.Fail("email.rejected");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "CampaignEmail SMTP send to {To} failed via {Provider}.", message.To, Code);
            return CampaignEmailSendResult.Fail("email.providerUnavailable");
        }
    }

    private static MimeMessage BuildMime(CampaignEmailOptions.SmtpOptions smtp, CampaignEmailMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(smtp.FromName ?? smtp.FromAddress, smtp.FromAddress!));
        mime.To.Add(new MailboxAddress(message.ToName ?? message.To, message.To));
        mime.Subject = string.IsNullOrWhiteSpace(message.Subject) ? "(no subject)" : message.Subject;

        // Message-Id = correlación campaña↔proveedor: el webhook del proveedor lo devuelve y de él se
        // recuperan tenant/campaign/run/dispatch para publicar el Delivered/Failed (sin tabla de mapeo).
        mime.MessageId = CampaignEmailCorrelation.BuildMessageId(
            message.TenantId,
            message.CampaignId,
            message.RunId,
            message.DispatchId
        );

        var builder = new BodyBuilder { HtmlBody = message.HtmlBody };
        if (!string.IsNullOrWhiteSpace(message.TextBody))
            builder.TextBody = message.TextBody;
        mime.Body = builder.ToMessageBody();
        return mime;
    }
}
