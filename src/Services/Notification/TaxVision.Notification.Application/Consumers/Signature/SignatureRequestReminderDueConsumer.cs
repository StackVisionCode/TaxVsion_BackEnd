using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Domain.Preferences;

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
        NotificationDispatcher dispatcher,
        ICustomerContactPolicyRepository contactPolicies,
        IOptions<PortalOptions> portal,
        IPlatformSettings platform,
        ITenantHostResolver hostResolver,
        ICorrelationContext correlation,
        ILogger<SignatureRequestReminderDueIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        var correlationId = ResolveCorrelationId(evt);
        using (correlation.Push(correlationId))
        {
            // Mismo enlace público, ahora bajo el subdominio de la oficina.
            var tenantHost = await hostResolver.ResolveHostAsync(evt.TenantId, ct);
            var inviteLink = TenantEmailLinks.SigningLink(tenantHost, portal.Value, evt.PublicToken);

            var prefersSms =
                string.Equals(evt.PreferredChannel, "Sms", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(evt.PhoneE164);

            // T7.3: respeta ContactPolicy del cliente si el firmante está mapeado. Externos (sin
            // MappedCustomerId) siguen recibiendo — es su único canal.
            if (
                !await CustomerContactPolicyFilter.AllowsForOptionalCustomerAsync(
                    evt.TenantId,
                    evt.MappedCustomerId,
                    prefersSms ? CustomerContactChannel.Sms : CustomerContactChannel.Email,
                    contactPolicies,
                    ct
                )
            )
                return;

            // Canal preferido del firmante: SMS con el enlace si lo eligió y tiene teléfono, si no email.
            if (prefersSms)
            {
                var body = SmsText.For(
                    SmsText.SignatureReminder,
                    evt.Language,
                    new Dictionary<string, string>
                    {
                        ["brand"] = platform.Current.BrandName,
                        ["fullName"] = evt.FullName,
                        ["link"] = inviteLink,
                    }
                );
                await dispatcher.SendSmsAsync(
                    evt.TenantId,
                    evt.PhoneE164!,
                    body,
                    NotificationCategory.DocumentsAndSignatures,
                    "sig.request_reminder_due.sms",
                    evt.EventId,
                    correlationId,
                    recipientUserId: null,
                    idempotencyToken: evt.EventId.ToString("N"),
                    ct: ct
                );
                return;
            }

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
                        ["invite_link"] = inviteLink,
                        ["expires_at"] = ScribeDates.Instant(evt.ExpiresAtUtc),
                        ["reminders_sent"] = evt.RemindersSent,
                        ["language"] = evt.Language,
                    },
                    ScribeRenderAudience.Client,
                    ScribeReader.From(evt.Language),
                    ct
                )
            ).EnsureRendered("sig.request_reminder_due.v1");

            var result = await gateway.QueueEmailAsync(
                render.ToDispatchRequest(
                    tenantId: evt.TenantId,
                    to: evt.Email,
                    templateKey: TemplateKey,
                    relatedEventId: evt.EventId,
                    correlationId: correlationId
                ),
                ct
            );

            if (result.IsSuccess)
            {
                logger.LogInformation(
                    "Reminder #{RemindersSent} dispatched to signer {SignerId} for request {RequestId}.",
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
