using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;

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
    private const string EventKey = "sig.signer_invited.v1";

    public static async Task Handle(
        SignerInvitedIntegrationEvent evt,
        IScribeRenderClient scribeClient,
        NotificationDispatcher dispatcher,
        IRecipientResolver recipientResolver,
        IRecipientLocale locales,
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
            // El firmante entra a SU oficina: la URL se arma con el subdominio del tenant, no un base fijo.
            var tenantHost = await hostResolver.ResolveHostAsync(evt.TenantId, ct);
            var inviteLink = TenantEmailLinks.SigningLink(tenantHost, portal.Value, evt.PublicToken);

            // Firmante externo: sin usuario no hay preferencia; el kind es obligatorio por catálogo.
            var signer = new NotificationTarget(
                Email: evt.Email,
                Phone: evt.PhoneE164,
                Locale: locales.FromEventLanguage(evt.Language)
            );

            // El render va antes de la primera escritura (la campana del portal); el SMS no lo necesita.
            // Hardening Fase 7: EnsureRendered lanza para que Wolverine reintente en vez de perder la invitación.
            var rendered =
                PrefersSms(evt) || string.IsNullOrWhiteSpace(evt.Email)
                    ? null
                    : (
                        await scribeClient.RenderAsync(
                            EventKey,
                            evt.TenantId,
                            new Dictionary<string, object?>
                            {
                                ["full_name"] = evt.FullName,
                                ["invite_link"] = inviteLink,
                                // F7 — null si el enlace nunca expira; el template Scribe decide cómo pintarlo.
                                ["expires_at"] = ScribeDates.Instant(evt.ExpiresAtUtc),
                                ["requires_consent"] = evt.RequiresConsent,
                                ["language"] = evt.Language,
                            },
                            ScribeRenderAudience.Client,
                            ScribeReader.From(evt.Language),
                            ct
                        )
                    ).EnsureRendered(EventKey);

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
                    await dispatcher.DispatchAsync(
                        Intent(evt, correlationId, new NotificationTarget(userId)) with
                        {
                            InApp = new InAppContent(
                                Params: new Dictionary<string, string> { ["fullName"] = evt.FullName },
                                ResourceId: evt.SignatureRequestId.ToString(),
                                ResourceContext: new Dictionary<string, string>
                                {
                                    ["customerId"] = customerId.ToString(),
                                }
                            ),
                        },
                        ct
                    );
                }
            }

            // Canal preferido del firmante: si eligió SMS y hay teléfono, la invitación va por SMS con el
            // enlace de firma; si no, se mantiene el correo. El puente publica al microservicio Sms.
            if (PrefersSms(evt))
            {
                // La fila guarda la clave, no el enlace.
                await dispatcher.DispatchAsync(
                    Intent(evt, correlationId, signer) with
                    {
                        Sms = new SmsContent(
                            SmsText.SignatureInvited,
                            "sig.signer_invited.sms",
                            new Dictionary<string, string>
                            {
                                ["brand"] = platform.Current.BrandName,
                                ["fullName"] = evt.FullName,
                                ["link"] = inviteLink,
                            },
                            evt.EventId.ToString("N")
                        ),
                    },
                    ct
                );
                logger.LogInformation(
                    "SignerInvited SMS dispatched to signer {SignerId} for request {RequestId}.",
                    evt.SignerId,
                    evt.SignatureRequestId
                );
                return;
            }

            var summary = await dispatcher.DispatchAsync(
                Intent(evt, correlationId, signer) with
                {
                    Email = new EmailContent(
                        EventKey,
                        new Dictionary<string, object?>(),
                        ScribeRenderAudience.Client,
                        TemplateKey: TemplateKey,
                        Render: (_, _) => Task.FromResult(rendered!)
                    ),
                },
                ct
            );

            if (summary.Channels.GetValueOrDefault(NotificationChannel.Email) != ChannelOutcome.Failed)
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
                    "SignerInvited email failed for signer {SignerId} for request {RequestId}.",
                    evt.SignerId,
                    evt.SignatureRequestId
                );
            }
        }
    }

    private static NotificationIntent Intent(
        SignerInvitedIntegrationEvent evt,
        string correlationId,
        NotificationTarget target
    ) =>
        new(
            NotificationKinds.SignatureSignerInvited,
            evt.TenantId,
            target,
            evt.EventId,
            "signature.signer.invited.v1",
            evt.OccurredOn,
            correlationId
        );

    private static bool PrefersSms(SignerInvitedIntegrationEvent evt) =>
        string.Equals(evt.PreferredChannel, "Sms", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(evt.PhoneE164);

    private static string ResolveCorrelationId(SignerInvitedIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;
}
