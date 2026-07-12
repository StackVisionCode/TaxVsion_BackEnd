using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Application.Consumers.Signature;

/// <summary>
/// Consume <see cref="SignerInvitedIntegrationEvent"/> emitido por Signature al enviar
/// la solicitud. Compone y envía el correo con el enlace público al firmante, y registra
/// el evento en <see cref="NotificationLog"/> para audit.
///
/// <para>
/// SRP: cada fase (log inicial, render, envío, transición estado) en un método privado
/// con nombre autoexplicativo. El consumer no accede a Signature ni conoce su BD — solo
/// recibe el evento y decide cómo entregar.
/// </para>
/// </summary>
public static class SignerInvitedConsumer
{
    private const string TemplateKey = SignatureTemplateCatalog.InvitationKey;

    public static async Task Handle(
        SignerInvitedIntegrationEvent evt,
        IEmailSender emailSender,
        INotificationLogRepository logRepository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SignerInvitedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        var correlationId = ResolveCorrelationId(evt);
        using (correlation.Push(correlationId))
        {
            var logResult = NotificationLog.Create(
                evt.TenantId,
                NotificationChannel.Email,
                evt.Email,
                BuildSubject(evt),
                TemplateKey,
                evt.EventId,
                correlationId
            );
            if (logResult.IsFailure)
            {
                logger.LogWarning(
                    "SignerInvited log could not be created for {SignerId}: {Error}",
                    evt.SignerId,
                    logResult.Error.Message
                );
                return;
            }

            var log = logResult.Value;
            await logRepository.AddAsync(log, ct);

            var message = BuildEmailMessage(evt);
            var sendResult = await emailSender.SendAsync(message, ct);
            if (sendResult.IsSuccess)
            {
                log.MarkSent();
                logger.LogInformation(
                    "SignerInvited email dispatched to signer {SignerId} for request {RequestId}.",
                    evt.SignerId,
                    evt.SignatureRequestId
                );
            }
            else
            {
                log.MarkFailed(sendResult.Error.Message);
                logger.LogWarning(
                    "SignerInvited email failed for signer {SignerId}: {Error}",
                    evt.SignerId,
                    sendResult.Error.Message
                );
            }

            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    // ------------------------------------------------------------------
    // Métodos privados: una única responsabilidad por método
    // ------------------------------------------------------------------

    private static string ResolveCorrelationId(SignerInvitedIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;

    private static string BuildSubject(SignerInvitedIntegrationEvent evt) =>
        SignatureTemplateCatalog
            .Invitation(evt.Language == "Es", evt.FullName, evt.PublicUrl, evt.ExpiresAtUtc, evt.RequiresConsent)
            .Subject;

    private static EmailMessage BuildEmailMessage(SignerInvitedIntegrationEvent evt)
    {
        var t = SignatureTemplateCatalog.Invitation(
            evt.Language == "Es",
            evt.FullName,
            evt.PublicUrl,
            evt.ExpiresAtUtc,
            evt.RequiresConsent
        );
        return new EmailMessage(evt.Email, t.Subject, t.Html, t.Text);
    }
}
