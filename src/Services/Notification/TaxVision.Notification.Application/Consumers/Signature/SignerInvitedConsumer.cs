using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers.Signature;

/// <summary>
/// Consume <see cref="SignerInvitedIntegrationEvent"/> emitido por Signature al enviar la solicitud.
/// Compone y despacha el correo con el enlace público al firmante, y registra el evento en
/// <see cref="TaxVision.Notification.Domain.Notifications.NotificationLog"/> para audit.
/// </summary>
/// <remarks>
/// Fase 8 (Scribe): el HTML ya no se arma localmente (antes: SignatureTemplateCatalog.Invitation) —
/// se renderiza en Scribe vía IScribeRenderClient y el resultado se envía tal cual por el gateway.
/// </remarks>
public static class SignerInvitedConsumer
{
    private const string TemplateKey = SignatureTemplateCatalog.InvitationKey;

    public static async Task Handle(
        SignerInvitedIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        ISmsSender smsSender,
        NotificationDispatcher dispatcher,
        IRecipientResolver recipientResolver,
        IOptions<PortalOptions> portal,
        IPlatformSettings platform,
        ITenantHostResolver hostResolver,
        ICorrelationContext correlation,
        ILogger<SignerInvitedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        var correlationId = ResolveCorrelationId(evt);
        using (correlation.Push(correlationId))
        {
            // T7.4.C — in-app en el portal del cliente si el firmante esta mapeado a un cliente
            // con cuenta de portal activa. Es ADITIVO al email/SMS: el firmante externo sigue solo
            // con email; el cliente del portal recibe email + in-app para que la invitacion le aparezca
            // en la campana al entrar. Node nunca procesó este evento, no hay dedup.
            if (evt.MappedCustomerId is { } customerId)
            {
                var portalUserId = await CustomerPortalRecipients.ResolveActivePortalUserAsync(
                    evt.TenantId,
                    customerId,
                    recipientResolver,
                    ct
                );
                if (portalUserId is { } userId)
                {
                    var title = $"Nueva solicitud de firma";
                    await dispatcher.RecordInAppAsync(
                        evt.TenantId,
                        $"user:{userId:N}",
                        title,
                        NotificationCategory.DocumentsAndSignatures,
                        "signature.signer.invited",
                        evt.EventId,
                        correlationId,
                        recipientUserId: userId,
                        inApp: new InAppNotificationRequest(
                            NotificationKinds.SignatureSignerInvited,
                            userId,
                            evt.EventId,
                            "signature.signer.invited.v1",
                            evt.OccurredOn,
                            title,
                            FallbackBody: "Hay un documento esperando tu firma.",
                            ResourceId: evt.SignatureRequestId.ToString(),
                            ResourceContext: new Dictionary<string, string> { ["customerId"] = customerId.ToString() },
                            Params: new Dictionary<string, string> { ["fullName"] = evt.FullName }
                        ),
                        ct: ct
                    );
                }
            }

            // El firmante entra a SU oficina: la URL se arma con el subdominio del tenant, no un base fijo.
            var tenantHost = await hostResolver.ResolveHostAsync(evt.TenantId, ct);
            var inviteLink = TenantEmailLinks.SigningLink(tenantHost, portal.Value, evt.PublicToken);

            // Canal preferido del firmante: si eligió SMS y hay teléfono, la invitación va por SMS con el
            // enlace de firma; si no, se mantiene el correo. El puente publica al microservicio Sms.
            if (PrefersSms(evt))
            {
                var smsBody = BuildInviteSms(evt, inviteLink, platform.Current.BrandName);
                var smsResult = await smsSender.SendAsync(
                    evt.PhoneE164!,
                    smsBody,
                    ct,
                    templateKey: null,
                    idempotencyToken: evt.EventId.ToString("N")
                );
                if (smsResult.IsSuccess)
                    logger.LogInformation(
                        "SignerInvited SMS dispatched to signer {SignerId} for request {RequestId}.",
                        evt.SignerId,
                        evt.SignatureRequestId
                    );
                else
                    logger.LogWarning(
                        "SignerInvited SMS failed for signer {SignerId}: {Error}",
                        evt.SignerId,
                        smsResult.Error
                    );
                return;
            }

            // Hardening Fase 7: un render fallido ya no se loguea-y-descarta — EnsureRendered lanza
            // ScribeRenderFailedException para que Wolverine reintente/DLQ en vez de que la
            // invitación de firma se pierda en silencio.
            var render = (
                await scribeClient.RenderAsync(
                    "sig.signer_invited.v1",
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["full_name"] = evt.FullName,
                        ["invite_link"] = inviteLink,
                        // F7 — null si el enlace nunca expira; el template Scribe decide cómo pintarlo.
                        ["expires_at"] = evt.ExpiresAtUtc?.ToString("yyyy-MM-dd HH:mm"),
                        ["requires_consent"] = evt.RequiresConsent,
                        ["language"] = evt.Language,
                    },
                    ct
                )
            ).EnsureRendered("sig.signer_invited.v1");

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
                    "SignerInvited email dispatched to signer {SignerId} for request {RequestId}.",
                    evt.SignerId,
                    evt.SignatureRequestId
                );
            }
            else
            {
                logger.LogWarning(
                    "SignerInvited email failed for signer {SignerId}: {Error}",
                    evt.SignerId,
                    result.Error
                );
            }
        }
    }

    private static bool PrefersSms(SignerInvitedIntegrationEvent evt) =>
        string.Equals(evt.PreferredChannel, "Sms", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(evt.PhoneE164);

    private static string BuildInviteSms(SignerInvitedIntegrationEvent evt, string inviteLink, string product)
    {
        return evt.Language == "Es"
            ? $"{product}: {evt.FullName}, tienes un documento para firmar. Ábrelo aquí: {inviteLink}"
            : $"{product}: {evt.FullName}, you have a document to sign. Open it here: {inviteLink}";
    }

    private static string ResolveCorrelationId(SignerInvitedIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;
}
