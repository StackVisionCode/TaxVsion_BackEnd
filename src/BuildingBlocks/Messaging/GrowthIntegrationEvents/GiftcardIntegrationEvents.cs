using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.GrowthIntegrationEvents;

/// <summary>
/// El PlatformAdmin pidió enviar una giftcard por correo. Notification arma y despacha el email: Growth no envía
/// correos ni llama a Notification por HTTP (evento de integración por RabbitMQ, sin M2M). El código viaja en claro
/// SOLO en este evento —el backend guarda únicamente su hash—, para el destinatario del correo.
/// </summary>
[MessageIdentity("growth.giftcards.share_requested.v1")]
public sealed record GiftcardShareRequestedIntegrationEvent : GrowthIntegrationEvent
{
    public override string EventType => "growth.giftcards.share_requested";
    public required string RecipientEmail { get; init; }
    public string? RecipientName { get; init; }

    /// <summary>"20% OFF" / "$25 OFF": el beneficio tal como se muestra en la tarjeta.</summary>
    public required string ValueLabel { get; init; }

    public required string Code { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }

    /// <summary>Mensaje personal opcional del remitente (se escapa al renderizar).</summary>
    public string? PersonalMessage { get; init; }

    /// <summary>Enlace al alta con el código precargado.</summary>
    public required string RedeemUrl { get; init; }
}
