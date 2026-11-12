using BuildingBlocks.Common;
using BuildingBlocks.Messaging.GrowthIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>
/// El PlatformAdmin envió una giftcard por correo (Growth publica el evento; nadie llama a Notification por HTTP).
/// Se renderiza la plantilla <c>giftcards.share_requested.v1</c> de Scribe y se despacha el email al destinatario.
/// </summary>
public static class GiftcardShareConsumer
{
    public static async Task Handle(
        GiftcardShareRequestedIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(string.Empty, evt.EventId)))
        {
            // Sin nombre se usa la parte local del correo, como el resto de correos de este servicio.
            var name = string.IsNullOrWhiteSpace(evt.RecipientName) ? evt.RecipientEmail.Split('@')[0] : evt.RecipientName!;
            var expires = evt.ExpiresAtUtc is { } until
                ? $"Valid until {until.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture)}"
                : "No expiry";

            var render = (
                await scribeClient.RenderAsync(
                    "giftcards.share_requested.v1",
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["recipient_name"] = name,
                        ["value_label"] = evt.ValueLabel,
                        ["code"] = evt.Code,
                        ["expires_text"] = expires,
                        ["redeem_url"] = evt.RedeemUrl,
                        ["personal_message"] = evt.PersonalMessage ?? string.Empty,
                    },
                    ct
                )
            ).EnsureRendered("giftcards.share_requested.v1");

            await gateway.QueueEmailAsync(
                render.ToDispatchRequest(
                    tenantId: evt.TenantId,
                    to: evt.RecipientEmail,
                    templateKey: "giftcards.share",
                    relatedEventId: evt.EventId,
                    correlationId: correlation.CorrelationId
                ),
                ct
            );
        }
    }
}
