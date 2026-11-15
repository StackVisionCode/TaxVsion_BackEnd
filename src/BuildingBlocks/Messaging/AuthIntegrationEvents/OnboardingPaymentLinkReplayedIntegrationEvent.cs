using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// Auth vuelve a anunciar el vínculo de un alta pagada: a qué oficina pertenece y qué recibo le generó. Lo
/// consume PaymentApp para completar pagos de alta viejos que quedaron sin oficina ni recibo (anteriores a
/// <c>OnboardingPaymentRehomeConsumer</c>). Evento propio a propósito: reenviar
/// <see cref="TenantCreatedForOnboardingIntegrationEvent"/> movería la saga y los correos de bienvenida.
/// <see cref="IntegrationEvent.TenantId"/> es la oficina del alta.
/// </summary>
[MessageIdentity("auth.onboarding.payment_link_replayed.v1")]
public sealed record OnboardingPaymentLinkReplayedIntegrationEvent : IntegrationEvent
{
    public required Guid OnboardingId { get; init; }
    public required Guid CreatedTenantId { get; init; }

    /// <summary>Recibo que Auth guardó para el alta; null si nunca se generó.</summary>
    public Guid? ReceiptFileId { get; init; }
}
