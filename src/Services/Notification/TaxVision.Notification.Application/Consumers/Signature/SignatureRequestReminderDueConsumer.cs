using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Consumers.Signature;

/// <summary>
/// Consume <see cref="SignatureRequestReminderDueIntegrationEvent"/> emitido por el
/// ReminderScheduler de Signature. Compone y despacha el correo de reminder usando el
/// template versionado <c>sig.reminder.v1</c>.
/// </summary>
/// <remarks>
/// Fase 8 (Scribe): el HTML ya no se arma localmente (antes: SignatureTemplateCatalog.Reminder) —
/// se renderiza en Scribe vía IScribeRenderClient y el resultado se envía tal cual por el gateway.
/// </remarks>
public static class SignatureRequestReminderDueConsumer
{
    private const string TemplateKey = SignatureTemplateCatalog.ReminderKey;

    public static async Task Handle(
        SignatureRequestReminderDueIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        ICorrelationContext correlation,
        ILogger<SignatureRequestReminderDueIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        var correlationId = ResolveCorrelationId(evt);
        using (correlation.Push(correlationId))
        {
            // Hardening Fase 7: un render fallido ya no se loguea-y-descarta — EnsureRendered lanza
            // ScribeRenderFailedException para que Wolverine reintente/DLQ en vez de que el
            // recordatorio de firma se pierda en silencio.
            var render = (
                await scribeClient.RenderAsync(
                    "sig.request_reminder_due.v1",
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["full_name"] = evt.FullName,
                        ["invite_link"] = evt.PublicUrl,
                        ["expires_at"] = evt.ExpiresAtUtc.ToString("yyyy-MM-dd HH:mm"),
                        ["reminders_sent"] = evt.RemindersSent,
                        ["language"] = evt.Language,
                    },
                    ct
                )
            ).EnsureRendered("sig.request_reminder_due.v1");

            var result = await gateway.QueueEmailAsync(
                new EmailDispatchRequest(
                    TenantId: evt.TenantId,
                    To: evt.Email,
                    Subject: render.Subject,
                    HtmlBody: render.Html,
                    TextBody: render.Text ?? string.Empty,
                    TemplateKey: TemplateKey,
                    RelatedEventId: evt.EventId,
                    CorrelationId: correlationId,
                    InlineAssets: render.InlineAssets
                ),
                ct
            );

            if (result.IsSuccess)
            {
                logger.LogInformation(
                    "Reminder {RemindersSent}/3 dispatched to signer {SignerId} for request {RequestId}.",
                    evt.RemindersSent,
                    evt.SignerId,
                    evt.SignatureRequestId
                );
            }
            else
            {
                logger.LogWarning(
                    "Reminder dispatch failed for signer {SignerId}: {Error}",
                    evt.SignerId,
                    result.Error
                );
            }
        }
    }

    private static string ResolveCorrelationId(SignatureRequestReminderDueIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;
}
