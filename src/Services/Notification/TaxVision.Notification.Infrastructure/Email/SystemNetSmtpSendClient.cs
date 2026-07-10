using System.Net;
using System.Net.Mail;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Infrastructure.Email;

/// <summary>
/// Envío SMTP con parámetros de conexión explícitos (System.Net.Mail, sin dependencias externas).
/// Sustituible por MailKit detrás de <see cref="ISmtpSendClient"/> sin tocar los handlers.
/// Devuelve <see cref="Result"/> (no lanza para fallos esperados de SMTP).
/// </summary>
public sealed class SystemNetSmtpSendClient(ILogger<SystemNetSmtpSendClient> logger) : ISmtpSendClient
{
    public async Task<Result> SendAsync(SmtpConnection connection, EmailMessage message, CancellationToken ct = default)
    {
        try
        {
            using var client = new SmtpClient(connection.Host, connection.Port)
            {
                EnableSsl = connection.UseSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
            };
            if (!string.IsNullOrWhiteSpace(connection.Username))
                client.Credentials = new NetworkCredential(connection.Username, connection.Password);

            using var mail = new MailMessage
            {
                From = new MailAddress(connection.FromEmail, connection.FromName),
                Subject = message.Subject,
                Body = message.HtmlBody,
                IsBodyHtml = true,
            };
            mail.To.Add(message.To);
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.TextBody, null, "text/plain"));

            await client.SendMailAsync(mail, ct);
            logger.LogInformation("SMTP send OK to {To} via {Host}.", message.To, connection.Host);
            return Result.Success();
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or FormatException)
        {
            logger.LogWarning(ex, "SMTP send FAILED to {To} via {Host}.", message.To, connection.Host);
            return Result.Failure(new Error("Email.SendFailed", ex.Message));
        }
    }
}
