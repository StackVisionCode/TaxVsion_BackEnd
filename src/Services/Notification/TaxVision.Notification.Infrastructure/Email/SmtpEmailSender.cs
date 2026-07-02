using System.Net;
using System.Net.Mail;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Infrastructure.Email;

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    /// <summary>Vacío ⇒ modo desarrollo: no se envía, se registra en el log.</summary>
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;
    public string? User { get; set; }
    public string? Password { get; set; }
    public bool EnableSsl { get; set; } = true;
    public string FromAddress { get; set; } = "no-reply@taxvision.local";
    public string FromName { get; set; } = "TaxVision";
}

/// <summary>
/// Envío SMTP con System.Net.Mail (sin dependencias externas; sustituible por
/// MailKit/SendGrid detrás de IEmailSender). Sin Host configurado opera en modo
/// desarrollo: registra el envío (el cuerpo solo a nivel Debug, pues contiene
/// enlaces con tokens).
/// </summary>
public sealed class SmtpEmailSender(
    IOptions<SmtpOptions> options,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly SmtpOptions _options = options.Value;

    public async Task<Result> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            logger.LogWarning(
                "SMTP no configurado (Smtp:Host vacío). Email '{Subject}' para {To} NO fue enviado.",
                message.Subject, message.To);
            logger.LogDebug(
                "[DEV] Cuerpo del email para {To}:\n{Body}",
                message.To, message.TextBody);
            return Result.Success();
        }

        try
        {
            using var client = new SmtpClient(_options.Host, _options.Port)
            {
                EnableSsl = _options.EnableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network
            };
            if (!string.IsNullOrWhiteSpace(_options.User))
                client.Credentials = new NetworkCredential(_options.User, _options.Password);

            using var mail = new MailMessage
            {
                From = new MailAddress(_options.FromAddress, _options.FromName),
                Subject = message.Subject,
                Body = message.HtmlBody,
                IsBodyHtml = true
            };
            mail.To.Add(message.To);
            mail.AlternateViews.Add(
                AlternateView.CreateAlternateViewFromString(
                    message.TextBody, null, "text/plain"));

            await client.SendMailAsync(mail, ct);
            return Result.Success();
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or FormatException)
        {
            return Result.Failure(new Error("Email.SendFailed", ex.Message));
        }
    }
}
