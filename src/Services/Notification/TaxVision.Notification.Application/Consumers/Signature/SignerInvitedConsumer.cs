using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;

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
        IOptions<PortalOptions> portal,
        ITenantHostResolver hostResolver,
        ICorrelationContext correlation,
        ILogger<SignerInvitedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        var correlationId = ResolveCorrelationId(evt);
        using (correlation.Push(correlationId))
        {
            // El firmante entra a SU oficina: la URL se arma con el subdominio del tenant, no un base fijo.
            var tenantHost = await hostResolver.ResolveHostAsync(evt.TenantId, ct);
            var inviteLink = TenantEmailLinks.SigningLink(tenantHost, portal.Value, evt.PublicToken);

            // Canal preferido del firmante: si eligió SMS y hay teléfono, la invitación va por SMS con el
            // enlace de firma; si no, se mantiene el correo. El puente publica al microservicio Sms.
            if (PrefersSms(evt))
            {
                var smsBody = BuildInviteSms(evt, inviteLink, portal.Value);
                var smsResult = await smsSender.SendAsync(evt.PhoneE164!, smsBody, ct);
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
                        ["expires_at"] = evt.ExpiresAtUtc.ToString("yyyy-MM-dd HH:mm"),
                        ["requires_consent"] = evt.RequiresConsent,
                        ["language"] = evt.Language,
                    },
                    ct
                )
            ).EnsureRendered("sig.signer_invited.v1");

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

    private static string BuildInviteSms(SignerInvitedIntegrationEvent evt, string inviteLink, PortalOptions portal)
    {
        var product = portal.ProductName;
        return evt.Language == "Es"
            ? $"{product}: {evt.FullName}, tienes un documento para firmar. Ábrelo aquí: {inviteLink}"
            : $"{product}: {evt.FullName}, you have a document to sign. Open it here: {inviteLink}";
    }

    private static string ResolveCorrelationId(SignerInvitedIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;
}
